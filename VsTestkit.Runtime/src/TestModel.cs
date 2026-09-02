// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Collections.Generic;
using System.Reflection;

namespace VsTestkit.Testing
{
    public class TestCase
    {
        public string Assembly;
        public string ClassName;
        public string MethodName;
        public string FullName => ClassName + "." + MethodName;

        public bool RequiresClient;
        public bool RequiresMultiplayer;
        public bool SingleplayerOnly;
        public string SkipReason;
        public int PlotSize = 16;
        public int PlotHeight = 32;
        public int TimeoutMs = 60000;

        internal MethodInfo Method;
        internal MethodInfo BeforeEach;
        internal MethodInfo AfterEach;
        internal Type DeclaringType;
    }

    public enum TestStatus { Passed, Failed, Errored, Skipped, TimedOut, NotRun }

    public class TestResult
    {
        public string name;
        public string className;
        public string method;
        public string status;
        public string message;
        public string stack;
        public long durationMs;
        public string plot;
        public List<string> output = new List<string>();

        public static TestResult For(TestCase tc, TestStatus status) => new TestResult
        {
            name = tc.FullName,
            className = tc.ClassName,
            method = tc.MethodName,
            status = status.ToString().ToLowerInvariant()
        };
    }

    public class RunSummary
    {
        public int total, passed, failed, errored, skipped, timedOut, notRun;
        public long durationMs;
        public bool aborted;
        public string abortReason;
        public List<TestResult> results = new List<TestResult>();

        public bool Ok => failed == 0 && errored == 0 && timedOut == 0 && !aborted;

        public void Tally(TestResult r)
        {
            results.Add(r);
            total++;
            switch (r.status)
            {
                case "passed":   passed++;   break;
                case "failed":   failed++;   break;
                case "errored":  errored++;  break;
                case "skipped":  skipped++;  break;
                case "timedout": timedOut++; break;
                default:         notRun++;   break;
            }
        }
    }

    /// <summary>Per-test captured output from Vs.Log().</summary>
    public static class TestOutput
    {
        static readonly object sync = new object();
        static List<string> current;

        internal static void Begin(List<string> sink)
        {
            lock (sync) current = sink;
        }

        internal static void End()
        {
            lock (sync) current = null;
        }

        public static void Write(string message)
        {
            lock (sync) current?.Add(message);
        }
    }
}
