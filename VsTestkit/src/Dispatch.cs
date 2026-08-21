using System;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace VsTestkit
{
    /// <summary>
    /// Marshals work from an RPC thread onto a game main thread and waits for it.
    ///
    /// Every verb that touches world state goes through here. Reading game objects
    /// straight off the HttpListener thread is a race, and the kind that surfaces
    /// as an intermittent test failure rather than a crash.
    /// </summary>
    public static class Dispatch
    {
        public const int DefaultTimeoutMs = 30000;

        public static T OnServer<T>(Func<T> fn, int timeoutMs = DefaultTimeoutMs)
        {
            var api = Hub.Sapi;
            if (api == null) throw new VerbException("server side is not attached", "no_server");
            return Run(api, fn, timeoutMs, "server");
        }

        public static T OnClient<T>(Func<T> fn, int timeoutMs = DefaultTimeoutMs)
        {
            var api = Hub.Capi;
            if (api == null) throw new VerbException("client side is not attached", "no_client");
            return Run(api, fn, timeoutMs, "client");
        }

        public static T OnSide<T>(string side, Func<T> fn, int timeoutMs = DefaultTimeoutMs)
        {
            switch ((side ?? "server").ToLowerInvariant())
            {
                case "server": return OnServer(fn, timeoutMs);
                case "client": return OnClient(fn, timeoutMs);
                default: throw new VerbException("unknown side '" + side + "', expected server or client", "bad_side");
            }
        }

        static T Run<T>(ICoreAPI api, Func<T> fn, int timeoutMs, string label)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            api.Event.EnqueueMainThreadTask(() =>
            {
                try { tcs.TrySetResult(fn()); }
                catch (Exception e) { tcs.TrySetException(e); }
            }, "vstestkit");

            // Wait on the handle rather than Task.Wait: Task.Wait throws the
            // fault wrapped in an AggregateException, which would bury a
            // VerbException and turn a clean 400 into a 500.
            if (!((IAsyncResult)tcs.Task).AsyncWaitHandle.WaitOne(timeoutMs))
            {
                throw new VerbException(
                    $"timed out after {timeoutMs}ms waiting for the {label} main thread " +
                    "(is the game paused, still loading, or shutting down?)", "timeout");
            }

            // GetAwaiter().GetResult() rethrows the original exception unwrapped.
            return tcs.Task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Runs an action on a game main thread without a return value.
        /// </summary>
        public static void OnSide(string side, Action fn, int timeoutMs = DefaultTimeoutMs)
        {
            OnSide<object>(side, () => { fn(); return null; }, timeoutMs);
        }
    }

    /// <summary>An error meant for the caller, not a stack trace.</summary>
    public class VerbException : Exception
    {
        public string Code { get; }

        public VerbException(string message, string code = "error") : base(message)
        {
            Code = code;
        }
    }
}
