using System;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VsTestkit.Testing
{
    /// <summary>
    /// The test player. Server-side authority, so these run on the server thread.
    /// </summary>
    public static class Player
    {
        public static IPlayer Me => Vs.RequireServer().World.AllOnlinePlayers.FirstOrDefaultOrNull();

        /// <summary>
        /// Puts the player within reach of a position and faces them at it.
        ///
        /// Plots sit well away from spawn, so without this every client-side test
        /// would aim at something two hundred blocks off and fail on reach.
        /// </summary>
        public static async Task StandNear(BlockPos target, double distance = 3)
        {
            var player = Require();

            // Stand back along -Z and a little above, so the target is in front of
            // and slightly below the camera - the natural angle for interaction.
            var to = new Vec3d(target.X + 0.5, target.Y + 1, target.Z + 0.5 + distance);
            player.Entity.TeleportTo(to);

            // Movement has to reach the client, and the client needs the chunks
            // around the new position, before anything can be aimed at.
            await Vs.Ticks(6);

            if (Vs.Capi != null) await Interact.LookAt(target);
        }

        public static Task Teleport(BlockPos pos) => Teleport(new Vec3d(pos.X + 0.5, pos.Y, pos.Z + 0.5));

        public static async Task Teleport(Vec3d pos)
        {
            Require().Entity.TeleportTo(pos);
            await Vs.Ticks(4);
        }

        public static async Task SetGameMode(EnumGameMode mode)
        {
            var r = await Vs.Cmd("/gamemode " + (mode == EnumGameMode.Creative ? "creative" : "survival"));
            if (r.Status != EnumCommandStatus.Success)
                throw new AssertionException("could not set game mode: " + r.StatusMessage);
        }

        /// <summary>Puts a stack in the active hotbar slot and selects it.</summary>
        public static async Task Hold(string code, int quantity = 1)
        {
            var player = Require();
            var slot = player.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = World.Stack(code, quantity);
            slot.MarkDirty();
            await Vs.Ticks(2);
        }

        public static ItemStack Held => Require().InventoryManager.ActiveHotbarSlot?.Itemstack;

        static IPlayer Require()
        {
            var player = Me;
            if (player == null)
                throw new AssertionException(
                    "no player is connected. Player helpers need a client attached - " +
                    "mark the test [RequiresClient] so it is skipped on a headless run.");
            return player;
        }
    }

    static class PlayerExt
    {
        internal static IPlayer FirstOrDefaultOrNull(this IPlayer[] players) =>
            players != null && players.Length > 0 ? players[0] : null;
    }
}
