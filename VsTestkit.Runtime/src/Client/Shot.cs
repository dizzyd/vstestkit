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

            for (var i = 0; i < maxTicks; i++)
            {
                await Vs.Ticks(1);

                var fresh = Existing().Except(before).ToList();
                if (fresh.Count == 0) continue;

                var newest = fresh.OrderByDescending(File.GetLastWriteTimeUtc).First();

                // The file appears before the write finishes often enough to
                // matter; a zero-length PNG is a confusing way to fail.
                if (new FileInfo(newest).Length == 0) continue;

                var dir = Path.GetDirectoryName(Path.GetFullPath(destination));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.Copy(newest, destination, true);
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
