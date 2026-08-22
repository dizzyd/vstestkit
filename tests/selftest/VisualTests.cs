// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System.IO;
using System.Threading.Tasks;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace VsTestkit.SelfTest
{
    /// <summary>
    /// The visual baseline machinery, tested against a scene this suite builds
    /// itself so the expected picture is not at the mercy of anything else.
    /// </summary>
    [RequiresClient]
    public class VisualTests
    {
        /// <summary>A checkerboard wall: high contrast, sharp edges, no animation.</summary>
        static void BuildWall()
        {
            for (var x = 0; x < 7; x++)
                for (var y = 1; y <= 4; y++)
                    World.SetBlock((x + y) % 2 == 0 ? "game:glass-plain" : "game:log-placed-oak-ud",
                        P(x + 4, y, 12));
        }

        static async Task Compose()
        {
            // Pin what the picture depends on. The harness freezes the clock, but
            // any earlier test calling Hours() moves the calendar for everyone -
            // and the month drives grass colour while the hour drives the sun. A
            // capture taken without pinning both matches when this test runs alone
            // and fails in a full suite, which is a maddening way to find out.
            await Cmd("/time setmonth jun");
            await Cmd("/time set day");
            BuildWall();
            await Ticks(4);

            await Player.Teleport(P(7, 1, 4));
            await Interact.LookAt(P(7, 2, 12));
            await Ticks(4);
        }

        [VsTest(TimeoutMs = 120000)]
        public async Task TheViewMatchesItsBaseline()
        {
            await Compose();
            await Visual.Match("checkerboard-wall");
        }

        [VsTest(TimeoutMs = 120000)]
        public async Task AChangedSceneIsDetected()
        {
            await Compose();

            // Record what this scene looks like, under a name of its own.
            await Visual.Match("change-detection");

            // Now change it, materially but not enormously: one column of the
            // wall swapped. If the comparison were vacuous - always passing, or
            // comparing an image with itself - this would still pass.
            for (var y = 1; y <= 4; y++) World.SetBlock("game:soil-medium-none", P(6, y, 12));
            await Ticks(4);

            var e = await Assert.ThrowsAsync<AssertionException>(
                () => Visual.Match("change-detection"));

            Assert.Contains(e.Message, "of pixels differ");
            Log(e.Message.Split('\n')[0]);

            // A failure has to leave something to look at.
            var diff = Path.Combine(Visual.ArtifactDir, "change-detection.diff.png");
            Assert.True(File.Exists(diff), "a diff image is written on failure: " + diff);
        }

        [VsTest]
        public async Task BaselinesAreKeyedByRenderer()
        {
            // Sharing one baseline across machines would mean one machine is
            // right and the rest are red: macOS runs a forward-compatible 4.1
            // context, Linux 4.6, and the rasterisers differ regardless.
            await Visual.EnsureRenderer();

            Assert.NotNull(Visual.Key, "renderer key");
            Assert.Contains(Visual.Key, RuntimeEnvOs());
            Assert.False(Visual.RendererName() == "unknown-renderer", "the renderer was identified");
            Log(await Visual.Describe());
        }

        static string RuntimeEnvOs() =>
            Vintagestory.API.Config.RuntimeEnv.OS.ToString().ToLowerInvariant();
    }
}
