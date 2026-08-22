// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VsTestkit
{
    /// <summary>
    /// Step 1 verb set: enough to drive and question a running server.
    /// </summary>
    public static class Verbs
    {
        public static object Invoke(string verb, Dictionary<string, object> args)
        {
            switch (verb)
            {
                case "ping":  return new { pong = true };
                case "info":  return Info();
                case "cmd":   return Cmd(args);
                case "eval":  return Eval(args);
                case "log":   return Log(args);
                case "stop":  return Stop();

                case "tests.load": return TestVerbs.Load(args);
                case "tests.list": return TestVerbs.List();
                case "tests.run":  return TestVerbs.Run(args);

                case "session.save": return SessionVerbs.Save(args);
                case "shot":         return ShotVerb.Take(args);

                default:
                    throw new VerbException(
                        $"unknown verb '{verb}'. known: ping, info, cmd, eval, log, stop, " +
                        "tests.load, tests.list, tests.run, shot, session.save", "unknown_verb");
            }
        }

        // ---------- args helpers ----------

        static string Str(Dictionary<string, object> a, string key, string fallback = null)
        {
            if (a.TryGetValue(key, out var v) && v != null) return Convert.ToString(v);
            if (fallback != null) return fallback;
            throw new VerbException($"missing required argument '{key}'", "bad_args");
        }

        static int Int(Dictionary<string, object> a, string key, int fallback)
        {
            if (a.TryGetValue(key, out var v) && v != null)
            {
                try { return Convert.ToInt32(v); } catch { throw new VerbException($"'{key}' must be an integer", "bad_args"); }
            }
            return fallback;
        }

        // ---------- info ----------

        static object Info()
        {
            var sides = Hub.AttachedSides();
            object server = null;

            if (Hub.Sapi != null)
            {
                server = Dispatch.OnServer<object>(() =>
                {
                    var sapi = Hub.Sapi;
                    return new
                    {
                        runPhase = sapi.Server.CurrentRunPhase.ToString(),
                        uptimeSeconds = sapi.Server.ServerUptimeSeconds,
                        isDedicated = sapi.Server.IsDedicated,
                        seed = sapi.WorldManager.Seed,
                        playStyle = sapi.WorldManager.CurrentPlayStyle?.Code,
                        saveGameName = sapi.WorldManager.SaveGame?.WorldName,
                        players = sapi.Server.Players.Select(p => new
                        {
                            name = p.PlayerName,
                            uid = p.PlayerUID,
                            state = p.ConnectionState.ToString(),
                            pos = p.Entity?.Pos?.XYZ?.ToString()
                        }).ToArray(),
                        loadedChunks = sapi.WorldManager.AllLoadedChunks?.Count ?? 0
                    };
                });
            }

            return new
            {
                sides,
                dataPath = Hub.DataPath,
                logPath = GamePaths.Logs,
                gameVersion = GameVersion.LongGameVersion,
                os = RuntimeEnv.OS.ToString(),
                server
            };
        }

        // ---------- cmd ----------

        /// <summary>
        /// Runs a chat command through ExecuteUnparsed, which - unlike piping to
        /// server stdin - hands back a structured result instead of only logging.
        /// </summary>
        static object Cmd(Dictionary<string, object> a)
        {
            var command = Str(a, "command");
            var asPlayer = a.TryGetValue("as", out var v) ? Convert.ToString(v) : "console";
            var timeoutMs = Int(a, "timeoutMs", Dispatch.DefaultTimeoutMs);

            if (!command.StartsWith("/")) command = "/" + command;

            // The command may complete asynchronously (deferred argument parsers),
            // so the result arrives on a callback rather than from the call itself.
            var tcs = new TaskCompletionSource<TextCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            Dispatch.OnServer<object>(() =>
            {
                var sapi = Hub.Sapi;
                var caller = BuildCaller(sapi, asPlayer);
                sapi.ChatCommands.ExecuteUnparsed(
                    command,
                    new TextCommandCallingArgs { Caller = caller },
                    r => tcs.TrySetResult(r));
                return null;
            }, timeoutMs);

            if (!tcs.Task.Wait(timeoutMs))
                throw new VerbException($"command did not complete within {timeoutMs}ms: {command}", "timeout");

            var result = tcs.Task.Result;
            return new
            {
                command,
                status = result?.Status.ToString(),
                success = result?.Status == EnumCommandStatus.Success,
                statusMessage = result?.StatusMessage,
                errorCode = result?.ErrorCode
            };
        }

        static Caller BuildCaller(ICoreServerAPI sapi, string asPlayer)
        {
            if (string.IsNullOrEmpty(asPlayer) || asPlayer == "console")
            {
                return new Caller
                {
                    Type = EnumCallerType.Console,
                    CallerRole = "admin",
                    CallerPrivileges = new[] { "*" },
                    FromChatGroupId = GlobalConstants.ConsoleGroup
                };
            }

            var player = sapi.Server.Players.FirstOrDefault(p =>
                string.Equals(p.PlayerName, asPlayer, StringComparison.OrdinalIgnoreCase) ||
                p.PlayerUID == asPlayer);

            if (player == null)
            {
                var known = string.Join(", ", sapi.Server.Players.Select(p => p.PlayerName));
                throw new VerbException(
                    $"no player '{asPlayer}' (connected: {(known.Length == 0 ? "none" : known)})", "no_player");
            }

            return new Caller
            {
                Player = player,
                FromChatGroupId = GlobalConstants.GeneralChatGroup
            };
        }

        // ---------- eval ----------

        static object Eval(Dictionary<string, object> a)
        {
            var code = Str(a, "code");
            var side = a.TryGetValue("side", out var v) ? Convert.ToString(v) : "server";
            var timeoutMs = Int(a, "timeoutMs", Dispatch.DefaultTimeoutMs);

            return Evaluator.Run(code, side, timeoutMs);
        }

        // ---------- log ----------

        static object Log(Dictionary<string, object> a)
        {
            var name = Str(a, "file", "server-main");
            if (!name.EndsWith(".log")) name += ".log";

            var path = Path.Combine(GamePaths.Logs, name);
            if (!File.Exists(path))
            {
                var available = Directory.Exists(GamePaths.Logs)
                    ? Directory.GetFiles(GamePaths.Logs, "*.log").Select(Path.GetFileName).ToArray()
                    : new string[0];
                throw new VerbException($"no log file '{name}' in {GamePaths.Logs}. available: {string.Join(", ", available)}", "no_log");
            }

            var lines = Int(a, "lines", 50);
            var pattern = a.TryGetValue("grep", out var g) && g != null ? Convert.ToString(g) : null;

            // The game holds these open for writing, so share both read and write.
            var all = new List<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null) all.Add(line);
            }

            IEnumerable<string> selected = all;
            if (pattern != null)
            {
                Regex re;
                try { re = new Regex(pattern, RegexOptions.IgnoreCase); }
                catch (Exception e) { throw new VerbException($"bad grep pattern: {e.Message}", "bad_args"); }
                selected = selected.Where(l => re.IsMatch(l));
            }

            var list = selected.ToList();
            var tail = list.Skip(Math.Max(0, list.Count - lines)).ToArray();

            return new { file = name, path, totalLines = all.Count, matched = list.Count, lines = tail };
        }

        // ---------- stop ----------

        static object Stop()
        {
            if (Hub.Sapi == null) throw new VerbException("server side is not attached", "no_server");

            // Answer before the process goes away, or the caller sees a dropped
            // connection instead of an acknowledgement.
            var t = new Thread(() =>
            {
                Thread.Sleep(250);
                try { Hub.Sapi?.Event.EnqueueMainThreadTask(() => Hub.Sapi?.Server.ShutDown(), "vstestkit-stop"); }
                catch { }
            }) { IsBackground = true, Name = "vstestkit-stop" };
            t.Start();

            return new { stopping = true };
        }
    }
}
