// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VsTestkit.Testing
{
    /// <summary>
    /// In-world interaction: aim at a block, then click it.
    ///
    /// Every method here aims and then <em>checks what the game actually
    /// selected</em> before sending the click. Aiming by trigonometry and hoping
    /// is how a test ends up quietly interacting with the wrong block, or with
    /// nothing, and then failing three assertions later somewhere unrelated.
    /// </summary>
    public static class Interact
    {
        /// <summary>Ticks to wait after aiming, for the selection raycast to catch up.</summary>
        public const int AimSettleTicks = 2;

        /// <summary>
        /// Points the camera at a world position.
        ///
        /// Two things make this less obvious than inverting EntityPos.GetViewVector,
        /// which is (-cosPitch*sinYaw, sinPitch, -cosPitch*cosYaw):
        ///
        /// Pitch is offset by pi. A level view is pitch ~= 3.1416, not 0, and
        /// ClientMain clamps it to [1.5858, 4.6974]. The naive solution
        /// asin(dy) lands near zero, outside that range entirely, so it aims
        /// somewhere unrelated. Taking pi - asin(dy) - the other solution of
        /// sin(pitch) = dy - lands mid-range and yaw becomes atan2(dx, dz).
        ///
        /// The angles live on the player entity, not on the mouse. MouseYaw and
        /// MousePitch are derived from EntityPlayer.Pos each frame while the
        /// window is focused, so writing only to them is discarded on the next
        /// frame. Both are set here so the aim holds whether or not the window
        /// has focus.
        /// </summary>
        public static async Task LookAt(Vec3d target)
        {
            await ClientSide.Run(() =>
            {
                var capi = Vs.Capi;
                var entity = capi.World.Player.Entity;
                var eye = EyePos(capi);

                var d = target.SubCopy(eye);
                var len = d.Length();
                if (len < 1e-6) throw new AssertionException("cannot look at the camera's own position");

                double dx = d.X / len, dy = d.Y / len, dz = d.Z / len;

                var pitch = (float)(Math.PI - Math.Asin(GameMath.Clamp(dy, -1, 1)));
                var yaw = (float)Math.Atan2(dx, dz);

                entity.Pos.Yaw = yaw;
                entity.Pos.Pitch = pitch;
                capi.Input.MouseYaw = yaw;
                capi.Input.MousePitch = pitch;

                // With the mouse ungrabbed the pick ray follows the cursor rather
                // than the crosshair, and an unfocused window leaves the cursor
                // wherever it was - typically 0,0. Aiming the camera then selects
                // nothing at all. Grabbing centres the cursor as a side effect,
                // which is the only reason this works on a focused window; an
                // unattended run has to do it explicitly.
                var game = ClientSide.Game;
                game.OnMouseMove(new MouseEvent(game.Width / 2, game.Height / 2));
            });

            await Vs.Ticks(AimSettleTicks);
        }

        public static Task LookAt(BlockPos pos, BlockFacing face = null) => LookAt(AimPoint(pos, face));

        /// <summary>
        /// Aims at a block and returns the resulting selection, failing with a
        /// useful message if the game did not select what was asked for.
        ///
        /// Polls rather than waiting a fixed number of ticks: the selection is
        /// recomputed in SystemMouseInWorldInteractions.OnFinalizeFrame, which is
        /// a *render* callback. An unfocused or occluded window renders far more
        /// slowly than it ticks, so a tick count that works on a focused window
        /// fails on an unattended one.
        /// </summary>
        public static async Task<BlockSelection> Aim(BlockPos pos, BlockFacing face = null, int maxTicks = 60)
        {
            await LookAt(pos, face);

            for (var i = 0; i < maxTicks; i++)
            {
                var sel = await ClientSide.Run(() => Vs.Capi.World.Player.CurrentBlockSelection);

                if (sel != null && sel.Position.Equals(pos) && (face == null || sel.Face == face))
                    return sel;

                await Vs.Ticks(1);
            }

            await Explain(pos, face);
            return null; // Explain always throws.
        }

        /// <summary>
        /// Turns "it did not work" into something that names the actual cause.
        /// </summary>
        static async Task Explain(BlockPos pos, BlockFacing face)
        {
            var detail = await ClientSide.Run(() =>
            {
                var capi = Vs.Capi;
                var sel = capi.World.Player.CurrentBlockSelection;
                var eye = EyePos(capi);
                var dist = eye.DistanceTo(AimPoint(pos, face));
                var reach = capi.World.Player.WorldData.PickingRange;

                // Any dialog preferring an ungrabbed mouse switches world picking
                // off entirely (ClientMain.UpdateFreeMouse), which looks exactly
                // like aiming at nothing.
                var blocking = capi.OpenedGuis
                    .OfType<GuiDialog>()
                    .Where(d => d.DialogType == EnumDialogType.Dialog && d.PrefersUngrabbedMouse)
                    .Select(d => d.GetType().Name)
                    .ToArray();

                if (blocking.Length > 0)
                    return $"an open dialog is suppressing world interaction: {string.Join(", ", blocking)}. " +
                           "Close it (Gui.CloseDialogs()) before interacting.";

                if (sel == null)
                    return dist > reach
                        ? $"nothing is selected; it is {dist:0.0} blocks away and reach is {reach:0.0} - " +
                          "call Player.StandNear(pos) first."
                        : $"nothing is selected, though it is {dist:0.0} blocks away and within reach - " +
                          "the target is probably air, or hidden behind another block.";

                if (!sel.Position.Equals(pos))
                    return $"selected {Show(sel.Position)} instead " +
                           $"({Vs.Sapi?.World.BlockAccessor.GetBlock(sel.Position)?.Code}) - something is in the way.";

                return $"hit the {sel.Face?.Code} face, not {face?.Code}.";
            });

            throw new AssertionException($"aimed at {Show(pos)} but {detail}");
        }

        /// <summary>Right-click a block: open a container, activate a mechanism, place from hand.</summary>
        public static async Task<BlockSelection> UseBlock(BlockPos pos, BlockFacing face = null, int holdFrames = 3)
        {
            var sel = await Aim(pos, face);
            await Input.Click(EnumMouseButton.Right, holdFrames);
            await Vs.Ticks(2);
            return sel;
        }

        /// <summary>
        /// Left-click and hold until the block is gone.
        ///
        /// Breaking takes as long as the block's material and the held tool say it
        /// does, so this holds and watches rather than guessing a tick count.
        /// </summary>
        public static async Task BreakBlock(BlockPos pos, int maxTicks = 400)
        {
            await Aim(pos);

            var before = World.BlockCode(pos);
            if (before == "game:air")
                throw new AssertionException($"nothing to break at {Show(pos)}");

            await Input.MouseDown(EnumMouseButton.Left);
            try
            {
                // Progress happens per frame, so wait on frames; the tick budget
                // is only the ceiling.
                for (var i = 0; i < maxTicks; i++)
                {
                    await Frames.Wait(1);
                    if (World.BlockCode(pos) != before) return;
                }
            }
            finally
            {
                await Input.MouseUp(EnumMouseButton.Left);
            }

            throw new AssertionException(
                $"{before} at {Show(pos)} did not break within {maxTicks} ticks " +
                "(wrong tool, or not actually being hit)");
        }

        /// <summary>The point aimed at: the centre of a face, or of the block.</summary>
        public static Vec3d AimPoint(BlockPos pos, BlockFacing face = null)
        {
            var centre = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);
            if (face == null) return centre;

            var n = face.Normalf;
            return centre.Add(n.X * 0.5, n.Y * 0.5, n.Z * 0.5);
        }

        internal static Vec3d EyePos(ICoreClientAPI capi)
        {
            var entity = capi.World.Player.Entity;
            return entity.Pos.XYZ.Add(entity.LocalEyePos);
        }

        static string Show(BlockPos p) => $"{p.X},{p.Y},{p.Z}";
    }
}
