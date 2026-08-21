using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using VsTestkit.Testing;

namespace VsTestkit
{
    /// <summary>
    /// Process-wide rendezvous between the two sides and the RPC server.
    ///
    /// The endpoint is off unless VSTESTKIT=1 is set in the environment. /eval
    /// compiles and runs arbitrary C# in the game process, so it must never come
    /// up by accident on a real server just because the mod got installed.
    /// </summary>
    public static class Hub
    {
        static readonly object sync = new object();

        public static ICoreServerAPI Sapi { get; private set; }
        public static ICoreClientAPI Capi { get; private set; }

        static RpcServer rpc;

        public static ILogger Logger =>
            (ICoreAPI)Sapi != null ? Sapi.Logger : Capi?.Logger;

        public static bool Enabled =>
            Environment.GetEnvironmentVariable("VSTESTKIT") == "1";

        public static void AttachServer(ICoreServerAPI api)
        {
            lock (sync)
            {
                Sapi = api;
                Testing.Vs.AttachServer(api);
                if (Enabled) SessionPrep.Install(api);
                Start(api);
            }
        }

        public static void AttachClient(ICoreClientAPI api)
        {
            lock (sync)
            {
                Capi = api;
                Testing.Vs.AttachClient(api);
                Start(api);
            }
        }

        static void Start(ICoreAPI api)
        {
            if (!Enabled)
            {
                api.Logger.Notification("[vstestkit] endpoint disabled (set VSTESTKIT=1 to enable)");
                return;
            }

            if (rpc != null)
            {
                // Second side joining an already-running endpoint. Rewrite the
                // handshake so callers can see which sides are live.
                rpc.WriteHandshake();
                api.Logger.Notification("[vstestkit] {0} side attached to endpoint on port {1}",
                    api.Side, rpc.Port);
                return;
            }

            try
            {
                rpc = new RpcServer();
                rpc.Start();
                api.Logger.Notification(
                    "[vstestkit] listening on http://127.0.0.1:{0}/  handshake: {1}",
                    rpc.Port, rpc.HandshakePath);
            }
            catch (Exception e)
            {
                api.Logger.Error("[vstestkit] failed to start endpoint: {0}", e);
                rpc = null;
            }
        }

        public static void Detach(string modid)
        {
            lock (sync)
            {
                // In singleplayer both sides dispose. Only tear the endpoint down
                // once, and only when nothing is left attached.
                Sapi = null;
                Capi = null;
                Testing.Vs.Detach();

                if (rpc != null)
                {
                    rpc.Stop();
                    rpc = null;
                }
            }
        }

        /// <summary>Sides currently attached, for /info and the handshake file.</summary>
        public static string[] AttachedSides()
        {
            lock (sync)
            {
                if (Sapi != null && Capi != null) return new[] { "server", "client" };
                if (Sapi != null) return new[] { "server" };
                if (Capi != null) return new[] { "client" };
                return new string[0];
            }
        }

        public static string DataPath => GamePaths.DataPath;
    }
}
