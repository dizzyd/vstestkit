// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Talks to the OTHER game process in a two-process multiplayer session.
    ///
    /// A singleplayer session has both sides in one process, so <c>OnServer()</c> is a
    /// thread switch. A real multiplayer session does not: the server is a separate
    /// process on the other end of a socket, and nothing in this process can touch its
    /// world. This is the bridge - it evaluates a snippet in the peer over the same RPC
    /// endpoint the shell tools use.
    ///
    /// The peer's port and token arrive as environment variables set by boot.sh when it
    /// launches the client, so a test never has to discover them.
    ///
    /// Only available in a session booted with <c>boot.sh --multiplayer</c>. Everywhere
    /// else <see cref="Available"/> is false and every call explains that rather than
    /// timing out against a port nobody is listening on.
    /// </summary>
    public static class Remote
    {
        // boot.sh runs the test client with HTTPS_PROXY/ALL_PROXY pointed at a closed port,
        // so the game can never reach the auth server. HttpClient honours those variables by
        // default, which would send this loopback call to the same dead port. The peer is on
        // 127.0.0.1; it needs no proxy and must not inherit that one.
        private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static readonly string PeerPort = Environment.GetEnvironmentVariable("VSTK_PEER_PORT") ?? "";
        private static readonly string PeerToken = Environment.GetEnvironmentVariable("VSTK_PEER_TOKEN") ?? "";

        /// <summary>True when this process was booted with a peer to talk to.</summary>
        public static bool Available => PeerPort.Length > 0 && PeerToken.Length > 0;

        /// <summary>
        /// Evaluates C# in the peer process and returns its result rendered as text.
        ///
        /// The snippet runs over there, so it sees that process's <c>sapi</c>/<c>capi</c>
        /// and its own loaded mods - not this one's. Keep it to a single expression or a
        /// short block ending in a return, the way <c>vstk eval</c> does.
        /// </summary>
        public static async Task<string> Eval(string code)
        {
            RequireAvailable();

            string body = "{\"code\":" + JsonString(code) + ",\"side\":\"server\"}";

            using HttpRequestMessage request = new(HttpMethod.Post,
                $"http://127.0.0.1:{PeerPort}/v1/eval");
            request.Headers.Add("X-Vstk-Token", PeerToken);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new AssertionException(
                    $"remote eval failed ({(int)response.StatusCode}): {text}");
            }

            return ValueOf(text);
        }

        /// <summary>
        /// Pulls result.value out of the RPC envelope, so a caller gets "7" rather than the
        /// JSON wrapper around it. Falls back to the whole body if it does not parse.
        /// </summary>
        private static string ValueOf(string body)
        {
            try
            {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("result", out var result)) return body;
                if (!result.TryGetProperty("value", out var value)) return body;

                return value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Null => body,
                    System.Text.Json.JsonValueKind.String => value.GetString() ?? "",
                    _ => value.ToString()
                };
            }
            catch (Exception)
            {
                return body;
            }
        }

        /// <summary>
        /// Runs a chat command on the peer, as the console. The peer is the server, so
        /// this is how a test changes world state it does not own.
        /// </summary>
        public static Task<string> Command(string command)
            => Eval($"sapi.ChatCommands.ExecuteUnparsed({JsonString(command)}, " +
                    "new Vintagestory.API.Common.TextCommandCallingArgs { Caller = new Vintagestory.API.Common.Caller { Type = Vintagestory.API.Common.EnumCallerType.Console } })");

        private static void RequireAvailable()
        {
            if (Available) return;

            throw new AssertionException(
                "no peer process to talk to. Remote.* needs a session booted with " +
                "'boot.sh --multiplayer', which runs the server and the client as separate " +
                "processes. In a singleplayer session use OnServer() instead - both sides " +
                "are in this process.");
        }

        /// <summary>Minimal JSON string escaping; the payload is one snippet of C#.</summary>
        private static string JsonString(string value)
        {
            StringBuilder sb = new("\"", value.Length + 16);

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
