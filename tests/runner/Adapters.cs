using System.Collections.Concurrent;

namespace VsTestkit.Testing
{
    // Only the game boundary is replaced: the runner, discovery, reports and
    // assertions are the production sources. Gates in the fixture control when
    // a game-thread continuation can finish without needing a running world.
    public sealed class GameContext : SynchronizationContext, IDisposable
    {
        readonly BlockingCollection<Action> queue = new();
        readonly Thread thread;

        public GameContext()
        {
            thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var action in queue.GetConsumingEnumerable()) action();
            });
            thread.Start();
        }

        public override void Post(SendOrPostCallback callback, object state) => queue.Add(() => callback(state));
        public void Dispose() { queue.CompleteAdding(); thread.Join(); }
    }

    public static class Vs
    {
        public static object Sapi => null;
        public static object Plot { get; set; }
        public static SynchronizationContext PrimaryCtx { get; set; }
        public static void Skip(string reason) => throw new SkipException(reason);
    }

    public static class Remote { public static bool Available => false; }
    public static class Plots
    {
        public static int SlotFor(string name) => 0;
        public static Task<object> Prepare(int slot, int size, int height) => throw new NotSupportedException();
    }
}

namespace Vintagestory.API.Common { }
namespace Vintagestory.API.MathTools { }
