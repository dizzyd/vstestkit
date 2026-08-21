using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Vintagestory.API.Config;

namespace VsTestkit
{
    /// <summary>
    /// Takes a screenshot on demand, outside a test.
    ///
    /// Testing.Shot is the version tests use; it waits on game ticks and so needs
    /// to run under the test SynchronizationContext. This one is driven from an
    /// RPC thread, which may simply block, so it polls the filesystem directly.
    /// Same underlying mechanism: fire the screenshot hotkey and wait for the
    /// file the game writes.
    /// </summary>
    public static class ShotVerb
    {
        public static object Take(Dictionary<string, object> a)
        {
            if (Hub.Capi == null)
                throw new VerbException("no client attached; there is nothing to photograph", "no_client");

            var dest = a.TryGetValue("path", out var p) ? Convert.ToString(p) : null;
            var timeoutMs = a.TryGetValue("timeoutMs", out var t) ? Convert.ToInt32(t) : 15000;

            var folder = GamePaths.Screenshots;
            var before = Existing(folder);

            Dispatch.OnClient<object>(() =>
            {
                var capi = Hub.Capi;
                if (!capi.Input.HotKeys.TryGetValue("screenshot", out var hotkey) || hotkey.Handler == null)
                    throw new VerbException("the screenshot hotkey has no handler; is the client loaded?", "not_ready");
                hotkey.Handler(hotkey.CurrentMapping);
                return null;
            });

            var deadline = Environment.TickCount64 + timeoutMs;
            long lastSize = -1;

            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(100);

                var fresh = Existing(folder).Except(before).ToList();
                if (fresh.Count == 0) continue;

                var newest = fresh.OrderByDescending(File.GetLastWriteTimeUtc).First();

                // The file lands before the write finishes; wait for the size to
                // settle rather than copying a half-written PNG.
                long size;
                try { size = new FileInfo(newest).Length; }
                catch (IOException) { continue; }
                if (size == 0 || size != lastSize) { lastSize = size; continue; }

                if (string.IsNullOrEmpty(dest))
                    return new { path = newest, bytes = size };

                var dir = Path.GetDirectoryName(Path.GetFullPath(dest));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                try { File.Copy(newest, dest, true); }
                catch (IOException) { continue; }

                return new { path = Path.GetFullPath(dest), source = newest, bytes = size };
            }

            throw new VerbException(
                $"no screenshot appeared in {folder} within {timeoutMs}ms. " +
                "Captures are throttled to one a second, so two in quick succession will do this.",
                "timeout");
        }

        static HashSet<string> Existing(string folder) =>
            Directory.Exists(folder)
                ? new HashSet<string>(Directory.GetFiles(folder, "*.png"))
                : new HashSet<string>();
    }
}
