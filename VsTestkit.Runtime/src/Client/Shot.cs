// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Config;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Screenshots, for looking at what a test actually rendered.
    /// </summary>
    public static class Shot
    {
        /// <summary>
        /// Where the game writes captures. Note this is the user's Pictures
        /// folder, not the run's data path - so a capture has to be copied out
        /// rather than found under --dataPath.
        /// </summary>
        public static string GameFolder => GamePaths.Screenshots;

        /// <summary>
        /// Takes a screenshot and copies it to <paramref name="destination"/>,
        /// returning that path.
        ///
        /// SystemScreenshot self-throttles to one capture per second and does the
        /// work on a later render stage, so this waits for a new file to appear
        /// rather than firing and hoping. The wait is tick-based, like everything
        /// else here.
        /// </summary>
        public static async Task<string> Take(string destination, int maxTicks = 400)
        {
            var before = Existing();

            await ClientSide.Run(() =>
            {
                var capi = Vs.Capi;
                if (!capi.Input.HotKeys.TryGetValue("screenshot", out var hotkey) || hotkey.Handler == null)
                    throw new AssertionException("the screenshot hotkey has no handler; is the client fully loaded?");
                hotkey.Handler(hotkey.CurrentMapping);
            });

            long lastSize = -1;

            for (var i = 0; i < maxTicks; i++)
            {
                await Vs.Ticks(1);

                var fresh = Existing().Except(before).ToList();
                if (fresh.Count == 0) continue;

                var newest = fresh.OrderByDescending(File.GetLastWriteTimeUtc).First();

                // The file appears in the directory before the game has finished
                // writing it. Wait for the size to stop changing, and treat a
                // locked file as "not ready yet" rather than as a failure - a
                // capture that is a few frames late is not a broken test.
                long size;
                try { size = new FileInfo(newest).Length; }
                catch (IOException) { continue; }

                if (size == 0 || size != lastSize) { lastSize = size; continue; }

                var dir = Path.GetDirectoryName(Path.GetFullPath(destination));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                try { File.Copy(newest, destination, true); }
                catch (IOException) { continue; }

                return Path.GetFullPath(destination);
            }

            throw new AssertionException(
                $"no screenshot appeared in {GameFolder} within {maxTicks} ticks. " +
                "Captures are throttled to one a second, so two in quick succession " +
                "will do this.");
        }

        static HashSet<string> Existing()
        {
            if (!Directory.Exists(GameFolder)) return new HashSet<string>();
            return new HashSet<string>(Directory.GetFiles(GameFolder, "*.png"));
        }
    }
}
