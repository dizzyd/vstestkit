// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;

namespace VsTestkit.Testing
{
    /// <summary>Marks a method as a test. Must return Task (async) or void.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class VsTestAttribute : Attribute
    {
        /// <summary>Overrides the per-test timeout, in milliseconds.</summary>
        public int TimeoutMs { get; set; } = 60000;
    }

    /// <summary>
    /// The test needs a client attached. Skipped, not failed, on a headless run -
    /// a server-only suite reporting a wall of failures teaches you nothing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class RequiresClientAttribute : Attribute { }

    /// <summary>
    /// Needs a two-process session (boot.sh --multiplayer): a real server on the other end
    /// of a socket, reachable with Remote. Skipped rather than failed in a singleplayer
    /// session, where client and server share one process and the sync code takes an
    /// entirely different branch.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class RequiresMultiplayerAttribute : Attribute { }

    /// <summary>
    /// The opposite: only meaningful when both sides share this process. Anything that
    /// calls OnServer(), or that assumes a player may edit server-authoritative state,
    /// belongs here - it is skipped in a two-process session rather than failing there.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class SingleplayerOnlyAttribute : Attribute { }

    /// <summary>Temporarily disable a test, with a reason that shows in the report.</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class SkipAttribute : Attribute
    {
        public string Reason { get; }
        public SkipAttribute(string reason) { Reason = reason; }
    }

    /// <summary>
    /// How much room this test needs. The runner gives every test its own plot so
    /// tests cannot tread on each other; oversize builds need to say so.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class PlotSizeAttribute : Attribute
    {
        public int Size { get; }
        public int Height { get; }
        public PlotSizeAttribute(int size, int height = 32) { Size = size; Height = height; }
    }

    /// <summary>Run before each test in the class.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class BeforeEachAttribute : Attribute { }

    /// <summary>Run after each test in the class, pass or fail.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class AfterEachAttribute : Attribute { }
}
