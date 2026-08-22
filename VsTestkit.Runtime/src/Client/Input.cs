// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Synthetic keyboard and mouse input.
    ///
    /// Events go into ClientMain.OnKeyDown / OnMouseDownRaw / OnMouseMove, which
    /// are the same entry points the windowing layer uses. So a hotkey, a GUI
    /// dialog and an in-world interaction all react exactly as they would to a
    /// person - no OS-level automation, and nothing that only works because the
    /// test asked nicely.
    /// </summary>
    public static class Input
    {
        public static Task KeyDown(GlKeys key, bool ctrl = false, bool shift = false, bool alt = false) =>
            ClientSide.Run(() => ClientSide.Game.OnKeyDown(Event(key, ctrl, shift, alt)));

        public static Task KeyUp(GlKeys key, bool ctrl = false, bool shift = false, bool alt = false) =>
            ClientSide.Run(() => ClientSide.Game.OnKeyUp(Event(key, ctrl, shift, alt)));

        /// <summary>Press and release, holding for a number of ticks in between.</summary>
        public static async Task Press(GlKeys key, int holdTicks = 1, bool ctrl = false, bool shift = false, bool alt = false)
        {
            await KeyDown(key, ctrl, shift, alt);
            await Vs.Ticks(holdTicks);
            await KeyUp(key, ctrl, shift, alt);
        }

        /// <summary>Types a character, for text fields.</summary>
        public static Task Type(char c) =>
            ClientSide.Run(() => ClientSide.Game.OnKeyPress(new KeyEvent { KeyChar = c }));

        public static async Task Type(string text)
        {
            foreach (var c in text) await Type(c);
        }

        /// <summary>
        /// Fires a hotkey by code, bypassing the key mapping.
        ///
        /// Use this when the point is what the hotkey does. Use Press when the
        /// point is the binding itself - which is the interesting case for a mod
        /// that rebinds or intercepts keys.
        /// </summary>
        public static Task<bool> Hotkey(string code) => ClientSide.Run(() =>
        {
            var capi = Vs.Capi;
            if (!capi.Input.HotKeys.TryGetValue(code, out var hotkey))
                throw new AssertionException($"no hotkey '{code}'");
            if (hotkey.Handler == null)
                throw new AssertionException($"hotkey '{code}' has no handler attached");

            return hotkey.Handler(hotkey.CurrentMapping);
        });

        /// <summary>
        /// Presses a mouse button for the world and the GUI.
        ///
        /// ClientMain.UpdateMouseButtonState is the entry point that matters:
        /// it raises the API mouse events, offers the event to every client
        /// system - which is how a GUI slot click lands - and then sets
        /// InWorldMouseState, which SystemMouseInWorldInteractions reads each
        /// frame to decide that a block is being used or broken.
        ///
        /// OnMouseDownRaw, despite being the literal entry point the windowing
        /// layer calls, only reaches the world through the hotkey mapping, and
        /// that path is conditional on AllowCharacterControl. Clicks sent that way
        /// look delivered and do nothing. Use RawMouseDown when the binding itself
        /// is what a test is about.
        /// </summary>
        public static Task MouseDown(EnumMouseButton button = EnumMouseButton.Left) =>
            ClientSide.Run(() => ClientSide.Game.UpdateMouseButtonState(button, true));

        public static Task MouseUp(EnumMouseButton button = EnumMouseButton.Left) =>
            ClientSide.Run(() => ClientSide.Game.UpdateMouseButtonState(button, false));

        /// <summary>
        /// Click, holding for a number of rendered frames.
        ///
        /// Frames, not ticks: SystemMouseInWorldInteractions notices a held button
        /// inside a render callback, so a press and release inside one tick can
        /// span no frame at all on a throttled window and simply never be seen.
        /// Charged interactions - chiselling, drawing a bow - want a longer hold.
        /// </summary>
        public static async Task Click(EnumMouseButton button = EnumMouseButton.Left, int holdFrames = 3)
        {
            await MouseDown(button);
            await Frames.Wait(holdFrames);
            await MouseUp(button);
        }

        /// <summary>
        /// The raw windowing-layer event, which goes through the key bindings.
        /// For tests about bindings rather than about the world.
        /// </summary>
        public static Task RawMouseDown(EnumMouseButton button = EnumMouseButton.Left) =>
            ClientSide.Run(() => ClientSide.Game.OnMouseDownRaw(MouseAt(button)));

        public static Task RawMouseUp(EnumMouseButton button = EnumMouseButton.Left) =>
            ClientSide.Run(() => ClientSide.Game.OnMouseUpRaw(MouseAt(button)));

        public static Task MouseMove(int x, int y) =>
            ClientSide.Run(() => ClientSide.Game.OnMouseMove(new MouseEvent(x, y)));

        static KeyEvent Event(GlKeys key, bool ctrl, bool shift, bool alt) => new KeyEvent
        {
            KeyCode = (int)key,
            CtrlPressed = ctrl,
            ShiftPressed = shift,
            AltPressed = alt
        };

        static MouseEvent MouseAt(EnumMouseButton button) =>
            new MouseEvent(Vs.Capi.Input.MouseX, Vs.Capi.Input.MouseY, button, 0);
    }
}
