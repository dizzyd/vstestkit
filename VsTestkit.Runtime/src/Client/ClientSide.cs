// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Shared plumbing for the client-side helpers.
    /// </summary>
    internal static class ClientSide
    {
        /// <summary>
        /// The concrete client. Its input entry points are public and are the ones
        /// GLFW calls, so synthetic events travel the same path as real ones.
        /// </summary>
        internal static ClientMain Game => (ClientMain)Vs.RequireClient().World;

        /// <summary>
        /// Runs a client-thread action and returns the caller to the side it
        /// started on.
        ///
        /// Tests begin on the server thread and mostly stay there, so requiring an
        /// explicit hop around every click would make them tedious and easy to get
        /// wrong. Landing the caller back where it started keeps that convenience
        /// from being surprising.
        /// </summary>
        internal static async Task<T> Run<T>(Func<T> fn)
        {
            var startedOnServer =
                (SynchronizationContext.Current as GameThreadContext)?.Side == EnumAppSide.Server;

            await Vs.OnClient();

            T result;
            try
            {
                result = fn();
            }
            catch
            {
                if (startedOnServer) await Vs.OnServer();
                throw;
            }

            if (startedOnServer) await Vs.OnServer();
            return result;
        }

        internal static async Task Run(Action fn) =>
            await Run<object>(() => { fn(); return null; });
    }
}
