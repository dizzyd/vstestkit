using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VsTestkit.Testing;

namespace VsTestkit
{
    /// <summary>
    /// Verbs for the compiled-suite tier: load an assembly, list what is in it,
    /// run it.
    /// </summary>
    public static class TestVerbs
    {
        public static object Load(Dictionary<string, object> a)
        {
            var path = Convert.ToString(a.TryGetValue("path", out var p) ? p : null);
            if (string.IsNullOrEmpty(path))
                throw new VerbException("missing required argument 'path'", "bad_args");

            if (!File.Exists(path))
                throw new VerbException($"no test assembly at {path}", "no_assembly");

            try
            {
                var cases = TestRunner.Load(path);
                return new
                {
                    path = TestRunner.LoadedPath,
                    count = cases.Count,
                    tests = cases.Select(Describe).ToArray()
                };
            }
            catch (VerbException) { throw; }
            catch (Exception e)
            {
                throw new VerbException($"could not load {path}: {e.Message}", "load_failed");
            }
        }

        public static object List()
        {
            return new
            {
                path = TestRunner.LoadedPath,
                count = TestRunner.Loaded.Count,
                tests = TestRunner.Loaded.Select(Describe).ToArray()
            };
        }

        public static object Run(Dictionary<string, object> a)
        {
            if (TestRunner.Loaded.Count == 0)
                throw new VerbException("no tests loaded - call tests.load first", "no_tests");

            if (Hub.Sapi == null)
                throw new VerbException("server side is not attached", "no_server");

            var filter = a.TryGetValue("filter", out var f) ? Convert.ToString(f) : null;
            var summary = TestRunner.Run(filter, Hub.Capi != null);

            return new
            {
                ok = summary.Ok,
                summary.total,
                summary.passed,
                summary.failed,
                summary.errored,
                summary.skipped,
                timedOut = summary.timedOut,
                notRun = summary.notRun,
                durationMs = summary.durationMs,
                summary.aborted,
                abortReason = summary.abortReason,
                clientAttached = Hub.Capi != null,
                results = summary.results
            };
        }

        static object Describe(TestCase t) => new
        {
            name = t.FullName,
            className = t.ClassName,
            method = t.MethodName,
            requiresClient = t.RequiresClient,
            skipReason = t.SkipReason,
            plotSize = t.PlotSize,
            timeoutMs = t.TimeoutMs
        };
    }
}
