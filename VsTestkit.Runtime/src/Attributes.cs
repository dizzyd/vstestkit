// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 dizzyd
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
