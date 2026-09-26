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

    CheckTruncated(Json.Write(new RecursiveResult()));
    for (int i = 0; i < 2; i++)
    {
        var response = Json.Read<JObject>(Json.Write(Evaluator.Run("new RecursiveResult()", "server", 1000)));
        CheckTruncated(response["value"]?.ToString());
        if ((string)response["type"] != typeof(RecursiveResult).FullName)
            throw new Exception("truncation lost the evaluated type: " + response);
    }
    Console.WriteLine("PASS: recursive results truncate in direct, compiled and cached serialization");

    object boundary = 42;
    for (int i = 0; i < Json.MaxDepth; i++) boundary = new[] { boundary };
    var token = JToken.Parse(Json.Write(boundary));
    var leaf = token;
    for (int i = 0; i < Json.MaxDepth; i++) leaf = leaf.Single();
    if ((int)leaf != 42) throw new Exception("the maximum permitted depth lost its value");
    CheckTruncated(Json.Write(new[] { boundary }));
    CheckTruncated(Json.Write(new JArray(token)));
    Console.WriteLine("PASS: the depth boundary preserves values and rejects deeper arrays and JSON tokens");

    var ordinary = Json.Read<JObject>(Json.Write(new FaultyResult()));
    if ((int?)ordinary["Value"] != 7 || ordinary.Property("Self") != null ||
        (int?)ordinary["AfterError"] != 9)
        throw new Exception("loop or member-error handling lost neighboring values: " + ordinary);
    Console.WriteLine("PASS: member errors and reference loops preserve neighboring values");
}
finally { queue.CompleteAdding(); thread.Join(); }

static void CheckTruncated(string json)
{
    var result = JObject.Parse(json ?? throw new Exception("missing truncation result"));
    if ((bool?)result["truncated"] != true || (string)result["reason"] != "max_depth" ||
        (int?)result["maxDepth"] != Json.MaxDepth)
        throw new Exception("expected an explicit depth-limit result: " + result);
}

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
    public class RecursiveResult
    {
        readonly int depth;
        public RecursiveResult(int depth = 1) { this.depth = depth; }

        // Keep a broken limiter from crashing the regression process itself.
        public RecursiveResult Next => depth > Json.MaxDepth
            ? throw new InvalidOperationException("traversed beyond the depth limit")
            : new RecursiveResult(depth + 1);
    }

    public class FaultyResult
    {
        public int Value => 7;
        public FaultyResult Self => this;
        public int Broken => throw new InvalidOperationException("unreadable member");
        public int AfterError => 9;
    }

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
