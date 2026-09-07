using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using VsTestkit;

var game = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? throw new Exception("set VINTAGE_STORY");
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    foreach (var dir in new[] { game, Path.Combine(game, "Lib"), Path.Combine(game, "Mods") })
    {
        var path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};
Assembly.Load("VSSurvivalMod"); // Evaluator's default imports include the loaded mod API.
using var queue = new BlockingCollection<Action>();
var events = DispatchProxy.Create<IServerEventAPI, EventProxy>();
((EventProxy)(object)events).Queue = queue;
var api = DispatchProxy.Create<ICoreServerAPI, ApiProxy>();
((ApiProxy)(object)api).Events = events;
Hub.Sapi = api;
var thread = new Thread(() => { foreach (var action in queue.GetConsumingEnumerable()) action(); });
thread.Start();
try
{
    // Exercise compilation and cached execution: getters and iterators must run
    // on the same thread as the snippet, not while writing the RPC response.
    for (int i = 0; i < 2; i++)
    {
        var response = Json.Read<JObject>(Json.Write(Evaluator.Run("new ThreadBoundResult()", "server", 1000)));
        if ((int?)response["value"]?["Value"] != 42 ||
            response["value"]?["Numbers"]?.ToString(Newtonsoft.Json.Formatting.None) != "[3,5]")
            throw new Exception("thread-bound result was lost: " + response);
        if ((string)response["type"] != typeof(ThreadBoundResult).FullName)
            throw new Exception("response no longer describes the snippet's original type");
    }
    Console.WriteLine("PASS: compiled and cached eval materialize thread-bound getters and iterators on the game thread");
}
finally { queue.CompleteAdding(); thread.Join(); }

public class ApiProxy : DispatchProxy
{
    public IServerEventAPI Events;
    protected override object Invoke(MethodInfo method, object[] args) =>
        method.Name == "get_Event" ? Events : throw new NotSupportedException(method.Name);
}
public class EventProxy : DispatchProxy
{
    public BlockingCollection<Action> Queue;
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name != "EnqueueMainThreadTask") throw new NotSupportedException(method.Name);
        Queue.Add((Action)args[0]);
        return null;
    }
}
namespace VsTestkit
{
    public static class Hub { public static ICoreServerAPI Sapi; public static ICoreClientAPI Capi; }
    public class ThreadBoundResult
    {
        readonly int owner = Environment.CurrentManagedThreadId;
        void CheckThread()
        {
            if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("wrong thread");
        }
        public int Value { get { CheckThread(); return 42; } }
        public IEnumerable<int> Numbers
        {
            get { CheckThread(); yield return 3; CheckThread(); yield return 5; }
        }
    }
}
