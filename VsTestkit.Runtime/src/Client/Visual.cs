// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkiaSharp;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace VsTestkit.Testing
{
    /// <summary>A rectangle of the frame to compare, in pixels.</summary>
    public readonly struct VisualRegion
    {
        public readonly int X, Y, Width, Height;

        public VisualRegion(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        /// <summary>A box in the middle of the frame - where the subject usually is.</summary>
        public static VisualRegion Centred(int width, int height, int frameWidth, int frameHeight) =>
            new VisualRegion((frameWidth - width) / 2, (frameHeight - height) / 2, width, height);

        public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
    }

    /// <summary>
    /// Visual regression: capture the view and compare it against a recorded
    /// baseline.
    ///
    /// Baselines are per-renderer, not per-project. macOS runs a
    /// forward-compatible GL 4.1 context and Linux gets 4.6; GLLineWidth and
    /// SmoothLines are no-ops on Mac and not on Linux; and llvmpipe, NVIDIA and
    /// Apple rasterise differently anyway. One shared baseline would mean one
    /// machine's is right and every other machine is red.
    ///
    /// Comparison is a tolerance over a count of differing pixels rather than a
    /// byte match, because even the same driver will not reproduce antialiasing
    /// exactly frame to frame.
    /// </summary>
    public static class Visual
    {
        /// <summary>
        /// Fraction of pixels allowed to differ before a test fails.
        ///
        /// Chosen from measurement, not taste. Repeat captures of an identical
        /// scene sit at 0.02-0.04% on Apple M4 and up to 0.32% on the GTX 1060,
        /// while a scene with one column of blocks changed comes out at 8.9%.
        /// 2% leaves six times the observed noise below it and still four times
        /// clear of the smallest real change measured - 0.5% would have been
        /// within a factor of two of NVIDIA's noise floor and eventually flaked.
        /// </summary>
        public const double DefaultTolerance = 0.02;

        /// <summary>Per-channel difference at which a pixel counts as different at all.</summary>
        public const int DefaultPixelThreshold = 12;

        /// <summary>
        /// Frames to let the scene settle before capturing.
        ///
        /// Generous on purpose. Chunk meshing runs on its own threads, so a
        /// capture taken shortly after a teleport into freshly loaded chunks
        /// catches terrain and blocks part-built. That was worth 2% of the frame
        /// on the first test of a run and 0.04% on a later one - the same scene,
        /// differing only in how long the client had had to draw it.
        /// </summary>
        public const int SettleFrames = 30;

        /// <summary>Ticks to let chunk meshing catch up before those frames.</summary>
        public const int SettleTicks = 20;

        static string rendererCache;

        /// <summary>
        /// Identifies which machine's rendering a baseline describes, e.g.
        /// "linux-nvidia-geforce-gtx-1060-6gb-pcie-sse2".
        ///
        /// Reading it is a GL call and so only legal on the client thread, but
        /// paths get built from sync code all over this class - so it is fetched
        /// once by EnsureRenderer and cached. Every entry point here awaits that
        /// first.
        /// </summary>
        public static string Key =>
            $"{RuntimeEnv.OS.ToString().ToLowerInvariant()}-{Slug(RendererName())}";

        public static string RendererName() => rendererCache ?? "unknown-renderer";

        /// <summary>Fetches the renderer string once, from the client thread.</summary>
        public static async Task EnsureRenderer()
        {
            if (rendererCache != null) return;

            rendererCache = await ClientSide.Run(() =>
            {
                var platform = (ClientPlatformWindows)ClientSide.Game.Platform;
                return platform.GetGraphicsCardRenderer();
            }) ?? "unknown-renderer";
        }

        /// <summary>Renderer, key and baseline directory - for logging from a test.</summary>
        public static async Task<string> Describe()
        {
            await EnsureRenderer();
            return $"renderer={RendererName()} key={Key} baselines={BaselineDir}";
        }

        /// <summary>Where baselines for this suite and this renderer live.</summary>
        public static string BaselineDir =>
            Path.Combine(SuiteDir(), "baselines", Key);

        /// <summary>Where a failing comparison leaves its evidence.</summary>
        public static string ArtifactDir =>
            Path.GetFullPath(Path.Combine(GamePaths.DataPath, "..", "results", "visual"));

        /// <summary>
        /// Captures the view with the HUD hidden, so a changing clock, health bar
        /// or minimap cannot fail a test about a block.
        /// </summary>
        public static async Task<string> Capture(string name)
        {
            await EnsureRenderer();

            var restore = await ClientSide.Run(() =>
            {
                var game = ClientSide.Game;
                var was = game.ShouldRender2DOverlays;
                game.ShouldRender2DOverlays = false;
                return was;
            });

            try
            {
                await Vs.Ticks(SettleTicks);
                await Frames.Wait(SettleFrames);

                Directory.CreateDirectory(ArtifactDir);
                return await Shot.Take(Path.Combine(ArtifactDir, name + ".actual.png"));
            }
            finally
            {
                await ClientSide.Run(() => ClientSide.Game.ShouldRender2DOverlays = restore);
            }
        }

        /// <summary>
        /// A centred box big enough for a built scene and small enough to leave
        /// out the sky and the far ground.
        ///
        /// The default for a reason: sky gradient and drifting cloud shadows move
        /// between runs and swamp everything else - a full-frame comparison of an
        /// unchanged scene came out at 35% differing pixels, none of it on the
        /// subject. Match uses this region unless an explicit region or
        /// wholeFrame: true is supplied.
        /// </summary>
        public static VisualRegion DefaultRegion(int frameWidth, int frameHeight) =>
            VisualRegion.Centred(frameWidth / 2, frameHeight / 2, frameWidth, frameHeight);

        /// <summary>
        /// Compares the current view against the baseline for this renderer.
        ///
        /// With no baseline recorded, writes one and passes with a note - the
        /// first run on a new machine records rather than fails. Set
        /// VSTK_UPDATE_BASELINES=1 to overwrite existing ones deliberately.
        /// </summary>
        public static async Task Match(
            string name,
            double tolerance = DefaultTolerance,
            int pixelThreshold = DefaultPixelThreshold,
            VisualRegion? region = null,
            bool wholeFrame = false)
        {
            var actual = await Capture(name);
            var baseline = Path.Combine(BaselineDir, name + ".png");

            if (Environment.GetEnvironmentVariable("VSTK_UPDATE_BASELINES") == "1" || !File.Exists(baseline))
            {
                var fresh = !File.Exists(baseline);
                Directory.CreateDirectory(BaselineDir);
                File.Copy(actual, baseline, true);

                Vs.Log(fresh
                    ? $"recorded a new baseline: {baseline}"
                    : $"updated baseline: {baseline}");
                return;
            }

            var result = Compare(baseline, actual, pixelThreshold, wholeFrame ? null : region ?? Auto(actual));

            if (result.SizeMismatch)
                throw new AssertionException(
                    $"{name}: the view is {result.ActualWidth}x{result.ActualHeight} but the baseline is " +
                    $"{result.BaselineWidth}x{result.BaselineHeight}. Window size is set in " +
                    "templates/clientsettings.json; a baseline recorded at another size cannot be compared.");

            if (result.Fraction <= tolerance)
            {
                Vs.Log($"{name}: {result.Fraction:P3} of pixels differ (tolerance {tolerance:P3})");
                return;
            }

            var diff = Path.Combine(ArtifactDir, name + ".diff.png");
            WriteDiff(baseline, actual, diff, pixelThreshold, wholeFrame ? null : region ?? Auto(actual));

            throw new AssertionException(
                $"{name}: {result.Fraction:P3} of pixels differ, tolerance is {tolerance:P3}.\n" +
                $"    baseline {baseline}\n" +
                $"    actual   {actual}\n" +
                $"    diff     {diff}\n" +
                "    If the change is intended, re-run with VSTK_UPDATE_BASELINES=1.");
        }

        // ---------- comparison ----------

        public struct Result
        {
            public bool SizeMismatch;
            public int BaselineWidth, BaselineHeight, ActualWidth, ActualHeight;
            public long Differing, Total;
            public double Fraction => Total == 0 ? 0 : (double)Differing / Total;
        }

        /// <summary>The default region, sized from the image actually captured.</summary>
        static VisualRegion? Auto(string imagePath)
        {
            using var bmp = SKBitmap.Decode(imagePath);
            return bmp == null ? (VisualRegion?)null : DefaultRegion(bmp.Width, bmp.Height);
        }

        public static Result Compare(string baselinePath, string actualPath, int pixelThreshold,
                                     VisualRegion? region = null)
        {
            using var a = Load(baselinePath);
            using var b = Load(actualPath);

            var r = new Result
            {
                BaselineWidth = a.Width, BaselineHeight = a.Height,
                ActualWidth = b.Width, ActualHeight = b.Height
            };

            if (a.Width != b.Width || a.Height != b.Height)
            {
                r.SizeMismatch = true;
                return r;
            }

            var box = Clamp(region, a.Width, a.Height);
            r.Total = (long)box.Width * box.Height;

            // Raw spans, not GetPixel. Per-pixel interop over half a million
            // pixels of two images was slow enough to matter and churned enough
            // memory to be worth avoiding in a process that is already large.
            var pa = a.GetPixelSpan();
            var pb = b.GetPixelSpan();
            var stride = a.Width * 4;

            for (var y = box.Y; y < box.Y + box.Height; y++)
            {
                var row = y * stride;
                for (var x = box.X; x < box.X + box.Width; x++)
                {
                    var i = row + x * 4;
                    if (Math.Abs(pa[i] - pb[i]) > pixelThreshold ||
                        Math.Abs(pa[i + 1] - pb[i + 1]) > pixelThreshold ||
                        Math.Abs(pa[i + 2] - pb[i + 2]) > pixelThreshold) r.Differing++;
                }
            }

            return r;
        }

        /// <summary>
        /// Decodes into a known layout, so pixels can be read as raw RGBA bytes
        /// rather than through per-pixel interop.
        /// </summary>
        static SKBitmap Load(string path)
        {
            using var decoded = SKBitmap.Decode(path);
            if (decoded == null) throw new AssertionException($"could not read {path} as an image");

            var info = new SKImageInfo(decoded.Width, decoded.Height,
                                       SKColorType.Rgba8888, SKAlphaType.Premul);
            var normalised = new SKBitmap(info);
            if (!decoded.ScalePixels(normalised, SKFilterQuality.None))
            {
                normalised.Dispose();
                throw new AssertionException($"could not normalise {path} for comparison");
            }
            return normalised;
        }

        /// <summary>
        /// Writes the actual capture with differing pixels painted magenta, so a
        /// failure can be looked at rather than reasoned about.
        /// </summary>
        static void WriteDiff(string baselinePath, string actualPath, string dest, int pixelThreshold,
                              VisualRegion? region)
        {
            using var a = Load(baselinePath);
            using var b = Load(actualPath);
            using var outBmp = new SKBitmap(new SKImageInfo(b.Width, b.Height,
                                            SKColorType.Rgba8888, SKAlphaType.Premul));

            var pa = a.GetPixelSpan();
            var pb = b.GetPixelSpan();
            var dst = outBmp.GetPixelSpan();
            var stride = b.Width * 4;
            var box = Clamp(region, b.Width, b.Height);

            unsafe
            {
                fixed (byte* d = &System.Runtime.InteropServices.MemoryMarshal.GetReference(dst))
                {
                    for (var y = 0; y < b.Height; y++)
                        for (var x = 0; x < b.Width; x++)
                        {
                            var i = y * stride + x * 4;
                            var inside = x >= box.X && x < box.X + box.Width &&
                                         y >= box.Y && y < box.Y + box.Height;

                            byte rr, gg, bb;
                            if (!inside)
                            {
                                // Outside the compared region everything is
                                // darkened, so the picture shows what was judged
                                // as well as how it differed.
                                rr = (byte)(pb[i] / 3); gg = (byte)(pb[i + 1] / 3); bb = (byte)(pb[i + 2] / 3);
                            }
                            else if (Math.Abs(pa[i] - pb[i]) > pixelThreshold ||
                                     Math.Abs(pa[i + 1] - pb[i + 1]) > pixelThreshold ||
                                     Math.Abs(pa[i + 2] - pb[i + 2]) > pixelThreshold)
                            {
                                rr = 255; gg = 0; bb = 255;
                            }
                            else
                            {
                                rr = (byte)(192 + pb[i] / 4); gg = (byte)(192 + pb[i + 1] / 4); bb = (byte)(192 + pb[i + 2] / 4);
                            }

                            d[i] = rr; d[i + 1] = gg; d[i + 2] = bb; d[i + 3] = 255;
                        }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            using var image = SKImage.FromBitmap(outBmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var stream = File.OpenWrite(dest);
            data.SaveTo(stream);
        }

        static VisualRegion Clamp(VisualRegion? region, int width, int height)
        {
            if (region == null) return new VisualRegion(0, 0, width, height);

            var r = region.Value;
            if (r.Width <= 0 || r.Height <= 0)
                throw new AssertionException($"visual region must have positive dimensions: {r}");

            // Intersect, rather than moving an off-screen rectangle onto the
            // image. Widen before adding so extreme coordinates cannot wrap.
            var left = Math.Max(0L, r.X);
            var top = Math.Max(0L, r.Y);
            var right = Math.Min((long)width, (long)r.X + r.Width);
            var bottom = Math.Min((long)height, (long)r.Y + r.Height);
            if (right <= left || bottom <= top)
                throw new AssertionException($"visual region {r} does not intersect the {width}x{height} image");
            return new VisualRegion((int)left, (int)top, (int)(right - left), (int)(bottom - top));
        }

        // ---------- paths ----------

        static string SuiteDir()
        {
            var dir = TestRunner.SuiteDir;
            if (string.IsNullOrEmpty(dir))
                throw new AssertionException(
                    "no suite directory is known, so there is nowhere to keep baselines. " +
                    "Visual.Match only works for a suite loaded from disk.");
            return dir;
        }

        static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }
    }
}
