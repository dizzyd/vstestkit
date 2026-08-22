// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 dizzyd
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace VsTestkit
{
    /// <summary>
    /// Saves the client's login out of a running session.
    ///
    /// A test run uses an ephemeral data path, so if the carried-in session is
    /// rejected and someone types a password at the game's own login screen, that
    /// new session dies with the run directory - and, because a fresh login
    /// supersedes the previous one, their other installs are now stale too. Worth
    /// rescuing.
    ///
    /// Reading it from the live client rather than from clientsettings.json on
    /// disk is deliberate: the game writes that file on a clean exit, and a test
    /// harness does not always give it one.
    ///
    /// The values are written straight to a file and never returned, so a
    /// credential does not end up in an RPC response, a terminal scrollback or a
    /// log.
    /// </summary>
    public static class SessionVerbs
    {
        // Same keys Cairn carries between packs (Cairn.Core/Launch/ClientSession).
        static readonly string[] Keys =
        {
            "sessionkey", "sessionsignature", "playeruid",
            "mptoken", "entitlements", "useremail", "playername"
        };

        const string Credential = "sessionkey";

        /// <summary>
        /// Saves the session as soon as the client attaches, if VSTK_SESSION_OUT
        /// names a file.
        ///
        /// On attach rather than on shutdown: a run that is killed, crashes, or is
        /// closed by hand never gets a clean stop, and a login typed at the game's
        /// own screen is exactly the thing you cannot afford to lose - every new
        /// login supersedes the last one, so losing it leaves the account with no
        /// working key anywhere.
        /// </summary>
        public static void AutoSave()
        {
            var path = Environment.GetEnvironmentVariable("VSTK_SESSION_OUT");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var result = Save(new Dictionary<string, object> { ["path"] = path });
                Hub.Logger?.Notification("[vstestkit] session: {0}", Json.Write(result));
            }
            catch (Exception e)
            {
                Hub.Logger?.Warning("[vstestkit] could not save session: {0}", e.Message);
            }
        }

        public static object Save(Dictionary<string, object> a)
        {
            var path = Convert.ToString(a.TryGetValue("path", out var p) ? p : null);
            if (string.IsNullOrEmpty(path))
                throw new VerbException("missing required argument 'path'", "bad_args");

            if (Hub.Capi == null)
                throw new VerbException("no client attached; there is no session to save", "no_client");

            var values = Dispatch.OnClient(() =>
            {
                var found = new Dictionary<string, string>();
                foreach (var key in Keys)
                {
                    string value = null;
                    try { value = Hub.Capi.Settings.String[key]; } catch { }
                    if (!string.IsNullOrEmpty(value)) found[key] = value;
                }
                return found;
            });

            if (!values.ContainsKey(Credential))
                return new { saved = false, reason = "the client is not logged in" };

            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonConvert.SerializeObject(values, Formatting.Indented));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            return new { saved = true, path = Path.GetFullPath(path), keys = values.Count };
        }
    }
}
