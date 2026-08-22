// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Loads compiled test assemblies, gives each test a private plot, and runs
    /// them one at a time on the game main thread.
    ///
    /// Serial by design: tests share one world, and a parallel run would trade a
    /// few seconds for failures that depend on interleaving.
    /// </summary>
    public static class TestRunner
    {
        /// <summary>Default ground block a cleared plot is floored with.</summary>
        public const string GroundBlock = "game:soil-medium-normal";

        static TestLoadContext loadContext;
        static readonly List<TestCase> loaded = new List<TestCase>();
        static string loadedPath;

        public static IReadOnlyList<TestCase> Loaded => loaded;
        public static string LoadedPath => loadedPath;

        /// <summary>
        /// Where the suite came from. Visual baselines live under it, so they
        /// travel with the tests that assert on them rather than with the machine
        /// that happened to record them.
        /// </summary>
        public static string SuiteDir { get; set; }

        // ---------- loading ----------

        /// <summary>
        /// Loads (or reloads) a test assembly.
        ///
        /// A collectible load context, from a byte copy rather than the file, so a
        /// rebuilt suite can be reloaded into a session that is already running -
        /// which is the whole point of keeping a session alive while iterating.
        /// </summary>
        public static IReadOnlyList<TestCase> Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"no test assembly at {path}", path);

            return LoadImage(File.ReadAllBytes(path), Path.GetFullPath(path));
        }

        /// <summary>
        /// Loads a compiled test assembly from memory and lists what is in it.
        ///
        /// From an image rather than a file so a rebuilt suite can replace a
        /// loaded one without the file being held, and so a suite compiled from
        /// source in-process - which never touches disk - loads the same way.
        /// </summary>
        public static IReadOnlyList<TestCase> LoadImage(byte[] image, string label)
        {
            loadContext?.Unload();
            loadContext = new TestLoadContext();

            Assembly asm;
            using (var ms = new MemoryStream(image))
            {
                asm = loadContext.LoadFromStream(ms);
            }

            loaded.Clear();
            loadedPath = label;

            foreach (var type in SafeTypes(asm))
            {
                var before = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                 .FirstOrDefault(m => m.GetCustomAttribute<BeforeEachAttribute>() != null);
                var after = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .FirstOrDefault(m => m.GetCustomAttribute<AfterEachAttribute>() != null);

                var classSkip = type.GetCustomAttribute<SkipAttribute>();
                var classClient = type.GetCustomAttribute<RequiresClientAttribute>() != null;
                var classPlot = type.GetCustomAttribute<PlotSizeAttribute>();

                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var attr = m.GetCustomAttribute<VsTestAttribute>();
                    if (attr == null) continue;

                    var skip = m.GetCustomAttribute<SkipAttribute>() ?? classSkip;
                    var plot = m.GetCustomAttribute<PlotSizeAttribute>() ?? classPlot;

                    loaded.Add(new TestCase
                    {
                        Assembly = Path.GetFileName(label),
                        ClassName = type.FullName,
                        MethodName = m.Name,
                        RequiresClient = classClient || m.GetCustomAttribute<RequiresClientAttribute>() != null,
                        SkipReason = skip?.Reason,
                        PlotSize = plot?.Size ?? 16,
                        PlotHeight = plot?.Height ?? 32,
                        TimeoutMs = attr.TimeoutMs,
                        Method = m,
                        BeforeEach = before,
                        AfterEach = after,
                        DeclaringType = type
                    });
                }
            }

            loaded.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return loaded;
        }

        static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                // A test class referencing a type the running game does not have
                // should not hide every other test in the assembly.
                return e.Types.Where(t => t != null);
            }
        }

        // ---------- running ----------

        public static RunSummary Run(string filter, bool clientAttached)
        {
            var summary = new RunSummary();
            var sw = Stopwatch.StartNew();

            var selected = loaded.Where(t => Matches(t, filter)).ToList();

            foreach (var tc in selected)
            {
                if (summary.aborted)
                {
                    summary.Tally(TestResult.For(tc, TestStatus.NotRun));
                    continue;
                }

                if (tc.SkipReason != null)
                {
                    var r = TestResult.For(tc, TestStatus.Skipped);
                    r.message = tc.SkipReason;
                    summary.Tally(r);
                    continue;
                }

                if (tc.RequiresClient && !clientAttached)
                {
                    var r = TestResult.For(tc, TestStatus.Skipped);
                    r.message = "needs a client; none attached";
                    summary.Tally(r);
                    continue;
                }

                summary.Tally(RunOne(tc, Plots.SlotFor(tc.FullName), summary));
            }

            summary.durationMs = sw.ElapsedMilliseconds;
            return summary;
        }

        static bool Matches(TestCase tc, string filter) =>
            string.IsNullOrEmpty(filter) ||
            tc.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Starts one test on the server main thread and waits for it here, on the
        /// caller's thread.
        ///
        /// The timeout is deliberately observed from outside the game thread: if a
        /// test wedges the game loop, a timeout scheduled on that same loop would
        /// never fire either.
        /// </summary>
        static TestResult RunOne(TestCase tc, int plotIndex, RunSummary summary)
        {
            var result = TestResult.For(tc, TestStatus.Passed);
            var sw = Stopwatch.StartNew();

            var tcs = new TaskCompletionSource<TestResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            Vs.ServerCtx.Post(_ =>
            {
                RunOneAsync(tc, plotIndex, result)
                    .ContinueWith(t => tcs.TrySetResult(result), TaskContinuationOptions.ExecuteSynchronously);
            }, null);

            // Budget beyond the test's own timeout to cover plot setup and teardown.
            var budget = tc.TimeoutMs + 30000;
            if (!((IAsyncResult)tcs.Task).AsyncWaitHandle.WaitOne(budget))
            {
                result.status = TestStatus.TimedOut.ToString().ToLowerInvariant();
                result.message =
                    $"did not finish within {budget}ms. The game thread may be blocked; " +
                    "remaining tests were not run because the world state is now unknown.";
                result.durationMs = sw.ElapsedMilliseconds;

                summary.aborted = true;
                summary.abortReason = $"{tc.FullName} timed out";
                return result;
            }

            result.durationMs = sw.ElapsedMilliseconds;
            return result;
        }

        static async Task RunOneAsync(TestCase tc, int plotIndex, TestResult result)
        {
            TestOutput.Begin(result.output);
            object instance = null;

            try
            {
                var plot = await Plots.Prepare(plotIndex, tc.PlotSize, tc.PlotHeight);
                Vs.Plot = plot;
                result.plot = plot.ToString();

                instance = Activator.CreateInstance(tc.DeclaringType);

                if (tc.BeforeEach != null) await Invoke(tc.BeforeEach, instance);
                await Invoke(tc.Method, instance);
            }
            catch (AssertionException e)
            {
                result.status = TestStatus.Failed.ToString().ToLowerInvariant();
                result.message = e.Message;
            }
            catch (Exception e)
            {
                var inner = Unwrap(e);
                if (inner is AssertionException)
                {
                    result.status = TestStatus.Failed.ToString().ToLowerInvariant();
                    result.message = inner.Message;
                }
                else
                {
                    result.status = TestStatus.Errored.ToString().ToLowerInvariant();
                    result.message = inner.GetType().Name + ": " + inner.Message;
                    result.stack = inner.StackTrace;
                }
            }
            finally
            {
                if (tc.AfterEach != null && instance != null)
                {
                    try { await Invoke(tc.AfterEach, instance); }
                    catch (Exception e)
                    {
                        result.output.Add("[AfterEach] " + Unwrap(e).Message);
                    }
                }

                Vs.Plot = null;
                TestOutput.End();
            }
        }

        /// <summary>
        /// Invokes a test method whether it is async or plain void, and unwraps the
        /// reflection wrapper so the failure reads as the test's own.
        /// </summary>
        static async Task Invoke(MethodInfo m, object instance)
        {
            object returned;
            try
            {
                returned = m.Invoke(instance, null);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }

            if (returned is Task task) await task;
        }

        static Exception Unwrap(Exception e)
        {
            while (true)
            {
                if (e is TargetInvocationException tie && tie.InnerException != null) { e = tie.InnerException; continue; }
                if (e is AggregateException ae && ae.InnerExceptions.Count == 1) { e = ae.InnerExceptions[0]; continue; }
                return e;
            }
        }
    }

    /// <summary>
    /// Collectible context for test assemblies. Resolving to null falls back to
    /// the default context, so VsTestkit.Runtime and the mod under test are the
    /// already-loaded instances and type identity matches.
    /// </summary>
    internal class TestLoadContext : AssemblyLoadContext
    {
        public TestLoadContext() : base("vstestkit-tests", isCollectible: true) { }
        protected override Assembly Load(AssemblyName assemblyName) => null;
    }
}
