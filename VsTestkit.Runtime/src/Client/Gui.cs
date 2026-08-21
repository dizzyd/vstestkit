using System;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Client;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Inspecting the client's open dialogs.
    ///
    /// Dialogs are live objects on the client thread, so a test can read a mod's
    /// own GuiDialog subclass and assert on its state directly rather than
    /// squinting at pixels.
    /// </summary>
    public static class Gui
    {
        /// <summary>Type names of everything currently open, HUDs included.</summary>
        public static Task<string[]> Open() => ClientSide.Run(() =>
            Vs.Capi.OpenedGuis.Select(g => g.GetType().Name).ToArray());

        /// <summary>Just the dialogs, without the always-present HUD elements.</summary>
        public static Task<string[]> OpenDialogs() => ClientSide.Run(() =>
            Vs.Capi.OpenedGuis
                .Select(g => g.GetType().Name)
                .Where(n => !n.StartsWith("Hud"))
                .ToArray());

        /// <summary>The open dialog of this type, or null.</summary>
        public static Task<T> Find<T>() where T : class => ClientSide.Run(() =>
            Vs.Capi.OpenedGuis.OfType<T>().FirstOrDefault());

        /// <summary>The open dialog of this type, failing with what <em>is</em> open.</summary>
        public static async Task<T> Require<T>() where T : class
        {
            var found = await Find<T>();
            if (found != null) return found;

            throw new AssertionException($"no open {typeof(T).Name}. " + await Explain<T>());
        }

        /// <summary>
        /// Says why a lookup found nothing.
        ///
        /// Calls out the same-short-name case specially. Vintage Story has more
        /// than one type sharing a short name across namespaces - the chest dialog
        /// is Vintagestory.API.Client.GuiDialogBlockEntityInventory, and there is
        /// a different GuiDialogBlockEntityInventory elsewhere - so a stray using
        /// directive binds the name to the wrong type and OfType quietly matches
        /// nothing. Without this, the failure reads as "the click did nothing".
        /// </summary>
        static async Task<string> Explain<T>() where T : class
        {
            var open = await ClientSide.Run(() =>
                Vs.Capi.OpenedGuis.Select(g => g.GetType()).ToArray());

            var sameName = open
                .Where(t => t.Name == typeof(T).Name && t != typeof(T))
                .Select(t => t.FullName + " (" + t.Assembly.GetName().Name + ")")
                .Distinct()
                .ToArray();

            if (sameName.Length > 0)
                return $"but a different type with the same name IS open: {string.Join(", ", sameName)}. " +
                       $"You asked for {typeof(T).FullName} ({typeof(T).Assembly.GetName().Name}) - " +
                       "check the using directives in the test.";

            var names = open.Select(t => t.Name).Where(n => !n.StartsWith("Hud")).ToArray();
            return "Open dialogs: " + (names.Length == 0 ? "none" : string.Join(", ", names));
        }

        public static async Task<bool> IsOpen<T>() where T : class => await Find<T>() != null;

        /// <summary>
        /// Waits for a dialog to appear. Opening one is a round trip to the server
        /// and back, so the tick count matters more than any wall-clock delay.
        /// </summary>
        public static async Task<T> WaitFor<T>(int maxTicks = 100) where T : class
        {
            for (var i = 0; i < maxTicks; i++)
            {
                var found = await Find<T>();
                if (found != null) return found;
                await Vs.Ticks(1);
            }

            throw new AssertionException(
                $"{typeof(T).Name} did not open within {maxTicks} ticks. " + await Explain<T>());
        }

        /// <summary>
        /// Closes every open dialog, leaving HUD elements alone.
        ///
        /// The runner does this before each test. Plots isolate world state, but
        /// not the client's UI - and a dialog left open by one test is not merely
        /// untidy: any dialog that prefers an ungrabbed mouse switches world
        /// interaction off, so the next test aims at things and selects nothing.
        /// </summary>
        public static Task CloseDialogs() => ClientSide.Run(() =>
        {
            foreach (var dialog in Vs.Capi.OpenedGuis.OfType<GuiDialog>().ToArray())
            {
                if (dialog.DialogType != EnumDialogType.Dialog) continue;
                try { dialog.TryClose(); } catch { }
            }
        });

        /// <summary>Waits for a dialog to go away.</summary>
        public static async Task WaitGone<T>(int maxTicks = 100) where T : class
        {
            for (var i = 0; i < maxTicks; i++)
            {
                if (await Find<T>() == null) return;
                await Vs.Ticks(1);
            }
            throw new AssertionException($"{typeof(T).Name} was still open after {maxTicks} ticks");
        }
    }
}
