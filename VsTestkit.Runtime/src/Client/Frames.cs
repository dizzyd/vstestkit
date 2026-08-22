// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 dizzyd
using System;
using System.Threading.Tasks;
using Vintagestory.API.Client;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Waiting on rendered frames rather than game ticks.
    ///
    /// Several things on the client only happen inside a render callback: the
    /// block-selection raycast, and the in-world reaction to a held mouse button,
    /// both live in SystemMouseInWorldInteractions.OnFinalizeFrame. Ticks and
    /// frames are not the same clock, and on an unattended window they are not
    /// even close - macOS throttles an occluded GL window hard, so a hundred ticks
    /// can pass with barely a frame between them.
    ///
    /// Anything that has to be *observed* by the renderer therefore waits on
    /// frames. Everything else still waits on ticks.
    /// </summary>
    public static class Frames
    {
        public static async Task Wait(int count = 1)
        {
            // Registering a renderer is main-thread-only, so the registration
            // itself has to happen over on the client side. Awaiting the result
            // afterwards resumes on whichever side the caller started from.
            var done = await ClientSide.Run(() =>
            {
                var capi = Vs.Capi;
                var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                capi.Event.RegisterRenderer(
                    new FrameCounter(Math.Max(1, count), capi, tcs), EnumRenderStage.Done, "vstk-frames");
                return tcs;
            });

            await done.Task;
        }

        class FrameCounter : IRenderer
        {
            readonly ICoreClientAPI capi;
            readonly TaskCompletionSource<object> done;
            int remaining;

            public FrameCounter(int count, ICoreClientAPI capi, TaskCompletionSource<object> done)
            {
                remaining = count;
                this.capi = capi;
                this.done = done;
            }

            public double RenderOrder => 1.0;
            public int RenderRange => 1;

            public void OnRenderFrame(float dt, EnumRenderStage stage)
            {
                if (--remaining > 0) return;
                capi.Event.UnregisterRenderer(this, EnumRenderStage.Done);
                done.TrySetResult(null);
            }

            public void Dispose() { }
        }
    }
}
