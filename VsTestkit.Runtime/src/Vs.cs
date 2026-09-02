// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VsTestkit.Testing
{
    /// <summary>
    /// The ambient test API. Tests do <c>using static VsTestkit.Testing.Vs;</c>.
    ///
    /// Everything here assumes it is called from a test body, which runs on a game
    /// main thread under a <see cref="GameThreadContext"/>. Calls from anywhere
    /// else fail with an explanation rather than racing.
    /// </summary>
    public static class Vs
    {
        public static ICoreServerAPI Sapi { get; private set; }
        public static ICoreClientAPI Capi { get; private set; }

        public static GameThreadContext ServerCtx { get; private set; }
        public static GameThreadContext ClientCtx { get; private set; }

        /// <summary>
        /// The thread a test body starts on: the server where there is one, otherwise the
        /// client. In a two-process multiplayer session the client's process has no server
        /// side at all - the world lives in the peer - so tests there run on the client and
        /// reach the server through <see cref="Remote"/>.
        /// </summary>
        public static GameThreadContext PrimaryCtx => ServerCtx ?? ClientCtx;

        /// <summary>The plot allocated to the running test.</summary>
        public static TestPlot Plot { get; internal set; }

        // ---------- wiring, called by the testkit mod ----------

        public static void AttachServer(ICoreServerAPI api)
        {
            Sapi = api;
            ServerCtx = new GameThreadContext(api, EnumAppSide.Server);
        }

        public static void AttachClient(ICoreClientAPI api)
        {
            Capi = api;
            ClientCtx = new GameThreadContext(api, EnumAppSide.Client);
        }

        public static void Detach()
        {
            Sapi = null; Capi = null;
            ServerCtx = null; ClientCtx = null;
            Plot = null;
        }

        // ---------- context ----------

        internal static GameThreadContext RequireContext()
        {
            if (SynchronizationContext.Current is GameThreadContext ctx) return ctx;

            throw new InvalidOperationException(
                "not running on a game thread. Test bodies run under the testkit's " +
                "SynchronizationContext; this usually means the method was called from " +
                "a Task.Run, a raw thread, or a callback that escaped the test body. " +
                "Use OnServer()/OnClient() to get back.");
        }

        internal static ICoreServerAPI RequireServer()
        {
            if (Sapi == null) throw new InvalidOperationException("server side is not attached");
            var ctx = RequireContext();
            if (ctx.Side != EnumAppSide.Server)
                throw new InvalidOperationException(
                    "this call needs server context - 'await OnServer();' first");
            return Sapi;
        }

        internal static ICoreClientAPI RequireClient()
        {
            if (Capi == null) throw new InvalidOperationException(
                "client side is not attached - mark the test [RequiresClient] so it is " +
                "skipped on a headless run instead of failing");
            var ctx = RequireContext();
            if (ctx.Side != EnumAppSide.Client)
                throw new InvalidOperationException(
                    "this call needs client context - 'await OnClient();' first");
            return Capi;
        }

        /// <summary>Hop to the server main thread. Subsequent awaits stay there.</summary>
        public static ContextSwitch OnServer()
        {
            if (ServerCtx == null) throw new InvalidOperationException("server side is not attached");
            return new ContextSwitch(ServerCtx);
        }

        /// <summary>Hop to the client main thread. Subsequent awaits stay there.</summary>
        public static ContextSwitch OnClient()
        {
            if (ClientCtx == null) throw new InvalidOperationException(
                "client side is not attached - mark the test [RequiresClient] so it is " +
                "skipped on a headless run instead of failing");
            return new ContextSwitch(ClientCtx);
        }

        // ---------- waiting ----------

        /// <summary>
        /// Wait for n game ticks on the current side.
        ///
        /// Always prefer this to a wall-clock delay: it only advances when the game
        /// loop does, so a stalled or throttled game blocks the test instead of
        /// letting it pass by accident. That matters on macOS, where an occluded
        /// window is throttled hard.
        /// </summary>
        public static Task Ticks(int count)
        {
            var api = RequireContext().Api;
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var remaining = Math.Max(1, count);
            var id = new long[1];

            id[0] = api.Event.RegisterGameTickListener(_ =>
            {
                if (--remaining > 0) return;
                api.Event.UnregisterGameTickListener(id[0]);
                tcs.TrySetResult(null);
            }, 1);

            return tcs.Task;
        }

        /// <summary>Advance the in-game calendar, then let one tick run.</summary>
        public static async Task Hours(double hours)
        {
            RequireServer().World.Calendar.Add((float)hours);
            await Ticks(1);
        }

        /// <summary>
        /// Poll a condition once per tick until it holds, or fail after a budget of
        /// ticks. For anything whose timing is not exactly known, this beats
        /// guessing a fixed Ticks() count that is either flaky or needlessly slow.
        /// </summary>
        public static async Task Until(Func<bool> condition, int maxTicks = 200, string what = null)
        {
            for (var i = 0; i < maxTicks; i++)
            {
                if (condition()) return;
                await Ticks(1);
            }

            throw new AssertionException(
                $"condition did not hold within {maxTicks} ticks" + (what != null ? $": {what}" : ""));
        }

        // ---------- positions ----------

        /// <summary>
        /// A position in the running test's plot. (0,0,0) is the plot's ground
        /// block; (0,1,0) is the air directly above it.
        /// </summary>
        public static BlockPos P(int x, int y, int z)
        {
            if (Plot == null)
                throw new InvalidOperationException("no plot allocated - P() is only valid inside a test");
            return Plot.At(x, y, z);
        }

        // ---------- shortcuts ----------

        public static T BE<T>(BlockPos pos) where T : BlockEntity => World.BE<T>(pos);

        /// <summary>Run a chat command as console, or as a named player.</summary>
        public static Task<TextCommandResult> Cmd(string command, string asPlayer = null)
        {
            var sapi = RequireServer();
            if (!command.StartsWith("/")) command = "/" + command;

            var caller = asPlayer == null
                ? new Caller
                {
                    Type = EnumCallerType.Console,
                    CallerRole = "admin",
                    CallerPrivileges = new[] { "*" },
                    FromChatGroupId = GlobalConstants.ConsoleGroup
                }
                : BuildPlayerCaller(sapi, asPlayer);

            var tcs = new TaskCompletionSource<TextCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            sapi.ChatCommands.ExecuteUnparsed(command, new TextCommandCallingArgs { Caller = caller },
                r => tcs.TrySetResult(r));
            return tcs.Task;
        }

        static Caller BuildPlayerCaller(ICoreServerAPI sapi, string name)
        {
            foreach (var p in sapi.Server.Players)
            {
                if (string.Equals(p.PlayerName, name, StringComparison.OrdinalIgnoreCase) || p.PlayerUID == name)
                    return new Caller { Player = p, FromChatGroupId = GlobalConstants.GeneralChatGroup };
            }
            throw new AssertionException($"no connected player named '{name}'");
        }

        /// <summary>Write a line into the test's captured output.</summary>
        public static void Log(string message) => TestOutput.Write(message);
    }

    /// <summary>Awaitable that moves execution to another game thread.</summary>
    public readonly struct ContextSwitch : INotifyCompletion
    {
        readonly GameThreadContext target;

        internal ContextSwitch(GameThreadContext target) { this.target = target; }

        public ContextSwitch GetAwaiter() => this;

        public bool IsCompleted =>
            SynchronizationContext.Current == target && target.IsOnThisThread();

        public void OnCompleted(Action continuation) => target.Post(_ => continuation(), null);

        public void GetResult() { }
    }
}
