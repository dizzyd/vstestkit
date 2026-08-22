// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace VsTestkit
{
    /// <summary>
    /// Loopback-only JSON endpoint. POST /v1/&lt;verb&gt; with a JSON body, get a
    /// JSON envelope back.
    ///
    /// Bound to 127.0.0.1 and gated on a per-run token written into the handshake
    /// file, which lives in the (ephemeral) data path so scripts can discover the
    /// port without being told it.
    /// </summary>
    public class RpcServer
    {
        public const int DefaultBasePort = 42999;
        public const int PortScanRange = 32;

        HttpListener listener;
        Thread thread;
        volatile bool running;

        public int Port { get; private set; }
        public string Token { get; private set; }
        public string HandshakePath => Path.Combine(Hub.DataPath, ".vstestkit");

        public void Start()
        {
            Token = Guid.NewGuid().ToString("N");

            var basePort = DefaultBasePort;
            var configured = Environment.GetEnvironmentVariable("VSTESTKIT_PORT");
            if (!string.IsNullOrEmpty(configured) && int.TryParse(configured, out var p)) basePort = p;

            listener = Bind(basePort, string.IsNullOrEmpty(configured) ? PortScanRange : 1);

            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "vstestkit-rpc" };
            thread.Start();

            WriteHandshake();
        }

        HttpListener Bind(int basePort, int range)
        {
            Exception last = null;
            for (var i = 0; i < range; i++)
            {
                var port = basePort + i;
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    l.Start();
                    Port = port;
                    return l;
                }
                catch (Exception e)
                {
                    last = e;
                    try { l.Close(); } catch { }
                }
            }
            throw new Exception($"could not bind any port in {basePort}..{basePort + range - 1}", last);
        }

        /// <summary>
        /// (Re)writes the handshake file. Called again when the second side
        /// attaches so `sides` reflects reality.
        /// </summary>
        public void WriteHandshake()
        {
            try
            {
                var payload = Json.Write(new
                {
                    port = Port,
                    token = Token,
                    pid = Environment.ProcessId,
                    sides = Hub.AttachedSides(),
                    dataPath = Hub.DataPath,
                    gameVersion = Vintagestory.API.Config.GameVersion.LongGameVersion
                });

                File.WriteAllText(HandshakePath, payload);
                Chmod600(HandshakePath);
            }
            catch (Exception e)
            {
                Hub.Logger?.Error("[vstestkit] could not write handshake file: {0}", e.Message);
            }
        }

        static void Chmod600(string path)
        {
            try
            {
                // The token is in here. Best effort - no equivalent on Windows.
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { }
        }

        void Loop()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch (Exception) { if (!running) return; continue; }

                // Each request gets its own thread: verbs block waiting on a game
                // main thread, and a slow one must not stall the next.
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            try
            {
                var verb = ctx.Request.Url.AbsolutePath.Trim('/');
                if (verb.StartsWith("v1/")) verb = verb.Substring(3);

                if (!Authorized(ctx))
                {
                    Respond(res, 401, new { ok = false, code = "unauthorized", error = "bad or missing X-Vstk-Token" });
                    return;
                }

                var body = ReadBody(ctx.Request);
                var args = string.IsNullOrWhiteSpace(body)
                    ? new Dictionary<string, object>()
                    : Json.Read<Dictionary<string, object>>(body) ?? new Dictionary<string, object>();

                var result = Verbs.Invoke(verb, args);
                Respond(res, 200, new { ok = true, result });
            }
            catch (VerbException ve)
            {
                Respond(res, 400, new { ok = false, code = ve.Code, error = ve.Message });
            }
            catch (Exception e)
            {
                Respond(res, 500, new
                {
                    ok = false,
                    code = "exception",
                    error = e.Message,
                    exceptionType = e.GetType().FullName,
                    stack = e.StackTrace
                });
            }
        }

        bool Authorized(HttpListenerContext ctx)
        {
            var given = ctx.Request.Headers["X-Vstk-Token"];
            return given != null && given == Token;
        }

        static string ReadBody(HttpListenerRequest req)
        {
            using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            return sr.ReadToEnd();
        }

        static void Respond(HttpListenerResponse res, int status, object payload)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(Json.Write(payload));
                res.StatusCode = status;
                res.ContentType = "application/json";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch { }
            finally
            {
                try { res.OutputStream.Close(); } catch { }
            }
        }

        public void Stop()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            try { listener?.Close(); } catch { }
            try { if (File.Exists(HandshakePath)) File.Delete(HandshakePath); } catch { }
        }
    }
}
