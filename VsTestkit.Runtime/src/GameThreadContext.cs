using System;
using System.Threading;
using Vintagestory.API.Common;

namespace VsTestkit.Testing
{
    /// <summary>
    /// A SynchronizationContext whose continuations run on a game main thread.
    ///
    /// This is what lets a test body read live block entities and GUI dialogs
    /// directly while still being able to <c>await Ticks(n)</c>: the whole body
    /// runs on the game thread, and only the await releases it.
    ///
    /// The context reinstalls itself around every callback. Without that, the
    /// first await would resume on the game thread with SynchronizationContext
    /// .Current unset, so the *second* await would capture nothing and silently
    /// move the rest of the test onto a thread pool thread - exactly the race the
    /// whole design exists to avoid.
    /// </summary>
    public class GameThreadContext : SynchronizationContext
    {
        readonly ICoreAPI api;

        public EnumAppSide Side { get; }
        public ICoreAPI Api => api;

        public GameThreadContext(ICoreAPI api, EnumAppSide side)
        {
            this.api = api;
            Side = side;
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            api.Event.EnqueueMainThreadTask(() => RunWithContext(d, state), "vstk-test");
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            if (IsOnThisThread())
            {
                RunWithContext(d, state);
                return;
            }

            using var done = new ManualResetEventSlim(false);
            Exception failure = null;

            api.Event.EnqueueMainThreadTask(() =>
            {
                try { RunWithContext(d, state); }
                catch (Exception e) { failure = e; }
                finally { done.Set(); }
            }, "vstk-test");

            done.Wait();
            if (failure != null) throw failure;
        }

        void RunWithContext(SendOrPostCallback d, object state)
        {
            var prev = Current;
            SetSynchronizationContext(this);
            try { d(state); }
            finally { SetSynchronizationContext(prev); }
        }

        public bool IsOnThisThread()
        {
            var id = Environment.CurrentManagedThreadId;
            return Side == EnumAppSide.Server
                ? id == Vintagestory.API.Config.RuntimeEnv.ServerMainThreadId
                : id == Vintagestory.API.Config.RuntimeEnv.MainThreadId;
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
