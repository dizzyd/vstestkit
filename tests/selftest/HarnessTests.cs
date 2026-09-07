// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace VsTestkit.SelfTest
{
    /// <summary>
    /// Tests the harness itself. Also the worked example of what a mod's suite
    /// looks like.
    /// </summary>
    public class HarnessTests
    {
        [VsTest]
        public async Task PlotIsClearedAndFloored()
        {
            Assert.Equal("game:soil-medium-normal", World.BlockCode(P(0, 0, 0)), "plot floor");
            Assert.Equal("game:air", World.BlockCode(P(0, 1, 0)), "above the floor");
            Assert.Equal("game:air", World.BlockCode(P(5, 10, 5)), "high corner");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task BlocksSurviveARoundTrip()
        {
            World.SetBlock("game:glass-plain", P(2, 1, 2));
            await Ticks(1);
            Assert.Equal("game:glass-plain", World.BlockCode(P(2, 1, 2)));
        }

        [VsTest]
        public async Task PlotsAreIsolatedFromEachOther()
        {
            // If plot allocation were broken, the block another test placed at the
            // same relative position would still be here.
            Assert.Equal("game:air", World.BlockCode(P(2, 1, 2)),
                "a neighbouring test's block leaked into this plot");
            World.SetBlock("game:glass-plain", P(2, 1, 2));
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task TicksAdvanceTheGameLoop()
        {
            var before = Sapi.Server.ServerUptimeMilliseconds;
            await Ticks(20);
            var after = Sapi.Server.ServerUptimeMilliseconds;

            Assert.Greater(after - before, 0L, "20 ticks should take measurable time");
        }

        [VsTest]
        public async Task TestBodyStaysOnTheServerThread()
        {
            var id = System.Environment.CurrentManagedThreadId;
            Assert.Equal(Vintagestory.API.Config.RuntimeEnv.ServerMainThreadId, id, "before await");

            await Ticks(1);
            Assert.Equal(Vintagestory.API.Config.RuntimeEnv.ServerMainThreadId,
                System.Environment.CurrentManagedThreadId, "after one await");

            // The second await is the one that catches a SynchronizationContext
            // that fails to reinstall itself.
            await Ticks(1);
            Assert.Equal(Vintagestory.API.Config.RuntimeEnv.ServerMainThreadId,
                System.Environment.CurrentManagedThreadId, "after a second await");
        }

        [VsTest]
        public async Task BlockEntitiesAreReachable()
        {
            // A chest is the simplest vanilla block entity with observable state.
            World.SetBlock("game:chest-east", P(3, 1, 3));
            await Ticks(2);

            var be = BE<BlockEntityGenericTypedContainer>(P(3, 1, 3));
            Assert.NotNull(be.Inventory, "chest inventory");
            Assert.Greater(be.Inventory.Count, 0, "chest slot count");
        }

        [VsTest]
        public async Task CommandsRunAndReport()
        {
            var r = await Cmd("/time set day");
            Assert.Equal(EnumCommandStatus.Success, r.Status, "/time set day");

            // Assert on the calendar, not on the formatted string: the clock keeps
            // moving between the command and the message being built, so "12:00"
            // renders as "11:59" often enough to be a coin flip.
            Assert.Contains(r.StatusMessage, "time set");
            Assert.Close(Sapi.World.Calendar.HourOfDay, 12.0, 0.5, "hour of day after /time set day");
        }

        [VsTest]
        public async Task CalendarCanBeAdvanced()
        {
            var before = Sapi.World.Calendar.TotalHours;
            await Hours(5);
            Assert.Close(Sapi.World.Calendar.TotalHours - before, 5.0, 0.5, "hours advanced");
        }

        [VsTest]
        public async Task UntilPollsUntilTrue()
        {
            var ticks = 0;
            var listener = Sapi.Event.RegisterGameTickListener(_ => ticks++, 1);
            try
            {
                await Until(() => ticks >= 3, 100, "three ticks to elapse");
                Assert.GreaterOrEqual(ticks, 3);
            }
            finally
            {
                Sapi.Event.UnregisterGameTickListener(listener);
            }
        }

        [VsTest]
        public async Task UntilIncludesTheLastPermittedTick()
        {
            var ready = false;
            var listener = Sapi.Event.RegisterGameTickListener(_ => ready = true, 1);
            try
            {
                await Until(() => ready, 1, "the first tick to run");
                Assert.True(ready);
                await Until(() => ready, 0);
                await Assert.ThrowsAsync<AssertionException>(() => Until(() => false, 0));
            }
            finally
            {
                Sapi.Event.UnregisterGameTickListener(listener);
            }
        }

        [VsTest]
        public async Task TickNowRequiresObservedCompletion()
        {
            var pos = P(4, 1, 4);
            var firings = 0;
            var listener = Sapi.Event.RegisterGameTickListener((world, at, dt) => firings++, pos, 60000);
            try
            {
                await Ticks(1);
                var before = firings;
                await Assert.ThrowsAsync<AssertionException>(() => World.TickNow(pos, maxTicks: 0));
                Assert.Equal(before, firings, "no tick has run with a zero budget");
                await World.TickNow(pos);
                Assert.Greater(firings, before, "the callback ran before TickNow returned");
            }
            finally
            {
                Sapi.Event.UnregisterGameTickListener(listener);
            }
        }

        [VsTest]
        public async Task EntitiesSpawnAndAreFound()
        {
            var e = World.SpawnEntity("game:chicken-hen", P(8, 1, 8));
            Assert.NotNull(e);
            await Ticks(2);

            var found = World.Entities(P(8, 1, 8), 5f, "chicken");
            Assert.Greater(found.Length, 0, "spawned chicken should be findable");
        }

        [VsTest]
        public async Task FailuresReportWhatWasExpected()
        {
            // Proves the failure path produces a useful message rather than a bare
            // "assertion failed". Deliberately caught so the suite stays green.
            var e = Assert.Throws<AssertionException>(() =>
                Assert.Equal("game:air", "game:stone", "block here"));

            Assert.Contains(e.Message, "block here");
            Assert.Contains(e.Message, "expected \"game:air\", was \"game:stone\"");
            await Task.CompletedTask;
        }

        [VsTest]
        [RequiresClient]
        public async Task NeedsAClient()
        {
            // Skipped on a headless run rather than failed; see ClientTests for
            // the client tier proper.
            Assert.NotNull(Capi, "client API");
            await Task.CompletedTask;
        }

        [VsTest]
        [Skip("demonstrates the skip attribute")]
        public async Task ExplicitlySkipped()
        {
            Assert.Fail("should have been skipped");
            await Task.CompletedTask;
        }
    }
}
