// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
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
        /// <summary>
        /// The absolute calendar position every capture is taken at - the same
        /// one the harness pins at startup, so this is normally a no-op and only
        /// matters if an earlier test moved time.
        ///
        /// Hour 12 of day 500, so midday: total hours modulo hoursPerDay is the
        /// time of day, and getting that wrong photographs the dark. 12000.5 -
        /// which looks like "midday-ish" and is not - produced baselines that
        /// were black rectangles full of stars, and one of them still "passed"
        /// because the capture matched it.
        /// </summary>
        const double PinnedHours = 500 * 24 + 12;

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
            // Pin absolute time, not just the time of day. The harness freezes
            // the clock, but any earlier test calling Hours() moves the calendar
            // for everyone, and "/time set day" fast-forwards to the next such
            // hour - so runs land on different absolute days. Sun angle, season
            // and cloud shadow all follow absolute time, and the last of those
            // alone was worth 18% of the frame.
            await World.SetCalendarTo(PinnedHours);
            await Ticks(10);          // the client's sky follows the jump
            BuildWall();
            await Ticks(4);

            await Player.Teleport(P(7, 1, 4));
            await Interact.LookAt(P(7, 2, 12));
            await Ticks(4);
        }

        /// <summary>
        /// Framed on the wall itself, not the default middle-of-the-frame box.
        ///
        /// Terrain colour is not stable across worlds - the same seed at the same
        /// pinned date renders green ground in one session and yellow in the next,
        /// while the built blocks come out pixel-identical. So a visual assertion
        /// frames its subject; anything else is asserting about scenery.
        /// </summary>
        static readonly VisualRegion Wall = new VisualRegion(290, 160, 380, 200);

        [VsTest(TimeoutMs = 120000)]
        public async Task TheViewMatchesItsBaseline()
        {
            await Compose();
            await Visual.Match("checkerboard-wall", region: Wall);
        }

        [VsTest(TimeoutMs = 120000)]
        public async Task AChangedSceneIsDetected()
        {
            await Compose();

            // This test measures change detection, not baseline-update policy.
            // Its before/after captures must never overwrite a committed baseline.
            var before = await Visual.Capture("change-before");

            // Now change it, materially but not enormously: one column of the
            // wall swapped. If the comparison were vacuous - always passing, or
            // comparing an image with itself - this would still pass.
            for (var y = 1; y <= 4; y++) World.SetBlock("game:soil-medium-none", P(6, y, 12));
            await Ticks(4);

            var after = await Visual.Capture("change-after");
            var comparison = Visual.Compare(before, after, Visual.DefaultPixelThreshold, Wall);
            Assert.False(comparison.SizeMismatch, "both captures use the same viewport");
            Assert.Greater(comparison.Fraction, Visual.DefaultTolerance,
                "the changed column must exceed the visual tolerance");
            Log($"{comparison.Fraction:P3} of pixels differ between the before and after captures");
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
