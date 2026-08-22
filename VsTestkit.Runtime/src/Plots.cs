// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Hands each test a private, cleared patch of world.
    ///
    /// Plots are laid out on a grid near spawn with a gap between them, so a test
    /// that spills - a falling block, a spreading fire, an entity that wanders -
    /// cannot reach its neighbours.
    /// </summary>
    public static class Plots
    {
        /// <summary>Distance between plot origins. Comfortably more than the default plot size.</summary>
        public const int Spacing = 48;

        /// <summary>Plots per row before wrapping.</summary>
        public const int Columns = 16;

        /// <summary>Kept clear of the spawn chunks so their contents never interfere.</summary>
        public const int BaseOffset = 128;

        /// <summary>
        /// Where the plot grid starts: the middle of the map.
        ///
        /// Deliberately not World.DefaultSpawnPosition. That is null for the first
        /// moments after world creation - SaveGameData.DefaultSpawn and
        /// mapMiddleSpawnPos are both unset, and EntityPosFromSpawnPos dereferences
        /// it - so a suite run straight after boot fails on whichever tests happen
        /// to go first. It can also return null legitimately, and /setspawn can move
        /// it mid-session, which would silently relocate every plot between runs.
        /// The map middle has none of those problems and is what spawn is derived
        /// from anyway.
        /// </summary>
        public static BlockPos Base(Vintagestory.API.Server.ICoreServerAPI sapi) =>
            new BlockPos(sapi.WorldManager.MapSizeX / 2, 0, sapi.WorldManager.MapSizeZ / 2, 0);

        public static async Task<TestPlot> Prepare(int index, int size, int height)
        {
            var sapi = Vs.RequireServer();
            var origin0 = Base(sapi);

            var col = index % Columns;
            var row = index / Columns;

            var x = origin0.X + BaseOffset + col * Spacing;
            var z = origin0.Z + BaseOffset + row * Spacing;

            var min = new BlockPos(x, 0, z, 0);
            var max = new BlockPos(x + size - 1, 0, z + size - 1, 0);

            // Load before measuring or writing: an unloaded chunk reports ground
            // height 0 and swallows block writes without erroring.
            await World.LoadArea(min, max);

            var groundY = World.GroundY(x, z);
            if (groundY <= 0)
                throw new AssertionException(
                    $"plot #{index} at {x},{z} has no terrain (ground height {groundY}). " +
                    "The chunk column did not load, so block writes there would be silently lost.");

            var origin = new BlockPos(x, groundY, z, 0);
            var plot = new TestPlot(origin, size, height, index);

            Clear(plot);
            await Vs.Ticks(1);

            // With a client attached, put the player in the plot. Plots sit well
            // away from spawn, so otherwise every interaction would aim at
            // something hundreds of blocks off; standing here also keeps the
            // surrounding chunks loaded on the client.
            if (Vs.Capi != null)
            {
                // A dialog left open by the previous test would suppress world
                // interaction for this one, so reset the UI too - plots isolate
                // the world, not the client.
                await Gui.CloseDialogs();

                await Player.Teleport(new Vintagestory.API.MathTools.Vec3d(
                    origin.X + size / 2.0, origin.Y + 1, origin.Z + size / 2.0));
            }

            return plot;
        }

        /// <summary>Air above, a flat floor at the origin layer.</summary>
        public static void Clear(TestPlot plot)
        {
            var o = plot.Origin;

            World.Fill(
                new BlockPos(o.X, o.Y + 1, o.Z, o.dimension),
                new BlockPos(o.X + plot.Size - 1, o.Y + plot.Height, o.Z + plot.Size - 1, o.dimension),
                null);

            World.Fill(
                o.Copy(),
                new BlockPos(o.X + plot.Size - 1, o.Y, o.Z + plot.Size - 1, o.dimension),
                TestRunner.GroundBlock);
        }
    }
}
