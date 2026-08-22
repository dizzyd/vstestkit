// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using Vintagestory.API.MathTools;

namespace VsTestkit.Testing
{
    /// <summary>
    /// A private patch of world for one test.
    ///
    /// Tests share a world - regenerating one per test would dominate the run - so
    /// isolation comes from space instead of time. Each test gets its own plot,
    /// cleared before it runs, far enough from its neighbours that nothing leaks
    /// across.
    /// </summary>
    public class TestPlot
    {
        /// <summary>Ground block of the plot. P(0,0,0) is exactly this.</summary>
        public BlockPos Origin { get; }

        public int Size { get; }
        public int Height { get; }
        public int Index { get; }

        public TestPlot(BlockPos origin, int size, int height, int index)
        {
            Origin = origin;
            Size = size;
            Height = height;
            Index = index;
        }

        public BlockPos At(int x, int y, int z) =>
            new BlockPos(Origin.X + x, Origin.Y + y, Origin.Z + z, Origin.dimension);

        /// <summary>Inclusive lower corner of the buildable volume (the ground layer).</summary>
        public BlockPos Min => Origin.Copy();

        /// <summary>Inclusive upper corner of the buildable volume.</summary>
        public BlockPos Max => At(Size - 1, Height, Size - 1);

        public override string ToString() =>
            $"plot#{Index} at {Origin.X},{Origin.Y},{Origin.Z} ({Size}x{Height}x{Size})";
    }
}
