// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace VsTestkit.SelfTest
{
    /// <summary>
    /// The client tier. Skipped entirely on a headless run.
    /// </summary>
    [RequiresClient]
    public class ClientTests
    {
        [VsTest]
        public async Task RunsOnlyWithAClient()
        {
            Assert.NotNull(Capi, "client API available in a [RequiresClient] test");
            Assert.NotNull(Player.Me, "a player is connected");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task PlayerIsInTheirPlot()
        {
            var pos = Player.Me.Entity.Pos.AsBlockPos;
            var origin = Plot.Origin;

            Assert.InRange(pos.X - origin.X, 0, Plot.Size, "player X within plot");
            Assert.InRange(pos.Z - origin.Z, 0, Plot.Size, "player Z within plot");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task AimingSelectsTheIntendedBlock()
        {
            World.SetBlock("game:glass-plain", P(4, 1, 4));
            await Ticks(2);
            await Player.StandNear(P(4, 1, 4));

            var sel = await Interact.Aim(P(4, 1, 4));
            Assert.Equal(P(4, 1, 4), sel.Position, "selected block");
        }

        [VsTest]
        public async Task AimingAtNothingFailsClearly()
        {
            await Player.StandNear(P(4, 1, 4));

            // P(4,8,4) is air with sky behind it, so nothing can be selected.
            // ThrowsAsync, not Throws with GetResult: blocking on a task from a
            // test body deadlocks the game thread the continuation needs.
            var e = await Assert.ThrowsAsync<AssertionException>(() => Interact.Aim(P(4, 8, 4)));

            Assert.Contains(e.Message, "nothing is selected");
        }

        [VsTest]
        public async Task SyntheticClickOpensAContainer()
        {
            World.SetBlock("game:chest-east", P(6, 1, 6));
            await Ticks(3);
            await Player.StandNear(P(6, 1, 6));

            await Interact.UseBlock(P(6, 1, 6));

            // A right click on a chest opens its dialog, through the same path a
            // real click takes.
            await Gui.WaitFor<Vintagestory.API.Client.GuiDialogBlockEntityInventory>(120);

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<Vintagestory.API.Client.GuiDialogBlockEntityInventory>(120);
        }

        [VsTest]
        public async Task BreakingRemovesTheBlock()
        {
            World.SetBlock("game:glass-plain", P(8, 1, 8));
            await Ticks(2);
            await Player.StandNear(P(8, 1, 8));

            await Interact.BreakBlock(P(8, 1, 8));
            Assert.Equal("game:air", World.BlockCode(P(8, 1, 8)), "block after breaking");
        }

        [VsTest]
        public async Task HotkeysFire()
        {
            // Named rather than typed, so this test does not drag another
            // assembly reference in just to mention a vanilla dialog.
            Assert.False((await Gui.OpenDialogs()).Contains("GuiDialogWorldMapDialog"),
                "the runner should have closed dialogs left by earlier tests");

            await Input.Hotkey("worldmapdialog");
            await Ticks(10);

            var open = await Gui.OpenDialogs();
            Assert.Greater(open.Length, 0, "the hotkey should have opened something: " + string.Join(", ", open));

            // Toggle it back. GuiDialogWorldMap is a HUD-type dialog, so the
            // runner's CloseDialogs deliberately leaves it alone.
            await Input.Hotkey("worldmapdialog");
            await Ticks(10);
        }

        [VsTest]
        public async Task HotkeysGuiLeakIsCleanedUpBetweenTests()
        {
            // Aiming has to keep working after an earlier test has been poking at
            // the UI; a dialog that prefers an ungrabbed mouse switches world
            // interaction off entirely, which looks like aiming at nothing.
            World.SetBlock("game:glass-plain", P(5, 1, 5));
            await Ticks(2);
            await Player.StandNear(P(5, 1, 5));

            var sel = await Interact.Aim(P(5, 1, 5));
            Assert.Equal(P(5, 1, 5), sel.Position, "aiming still works after a dialog leak");
        }

        [VsTest]
        public async Task ScreenshotsCanBeTaken()
        {
            World.SetBlock("game:glass-plain", P(2, 1, 2));
            await Ticks(2);
            await Player.StandNear(P(2, 1, 2));

            var path = await Shot.Take(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "vstestkit-selftest.png"));

            Assert.True(System.IO.File.Exists(path), "screenshot file exists");
            Assert.Greater(new System.IO.FileInfo(path).Length, 1000L, "screenshot size in bytes");
            Log("screenshot: " + path);
        }

        [VsTest]
        public async Task ClientAndServerThreadsAreDistinct()
        {
            Assert.Equal(Vintagestory.API.Config.RuntimeEnv.ServerMainThreadId,
                System.Environment.CurrentManagedThreadId, "tests start on the server thread");

            await OnClient();
            Assert.Equal(Vintagestory.API.Config.RuntimeEnv.MainThreadId,
                System.Environment.CurrentManagedThreadId, "after OnClient");

            await Ticks(1);
            Assert.Equal(Vintagestory.API.Config.RuntimeEnv.MainThreadId,
                System.Environment.CurrentManagedThreadId, "client context survives an await");

            await OnServer();
            Assert.Equal(Vintagestory.API.Config.RuntimeEnv.ServerMainThreadId,
                System.Environment.CurrentManagedThreadId, "after OnServer");
        }
    }
}
