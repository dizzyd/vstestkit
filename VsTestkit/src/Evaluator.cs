// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace VsTestkit
{
    /// <summary>
    /// Compiles and runs a C# snippet inside the game process.
    ///
    /// The game already ships Roslyn to build source mods, and Lib/ is on the mod
    /// assembly search path, so this costs no extra dependency. References are
    /// taken from whatever is currently loaded rather than a fixed list, which
    /// means the mod under test is automatically in scope the moment it loads.
    /// </summary>
    public static class Evaluator
    {
        static readonly ConcurrentDictionary<string, MethodInfo> cache =
            new ConcurrentDictionary<string, MethodInfo>();

        static readonly string[] DefaultUsings =
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "Vintagestory.API.Common",
            "Vintagestory.API.Common.Entities",
            "Vintagestory.API.Client",
            "Vintagestory.API.Server",
            "Vintagestory.API.Config",
            "Vintagestory.API.Datastructures",
            "Vintagestory.API.MathTools",
            "Vintagestory.API.Util",
            "Vintagestory.GameContent",
            "VsTestkit"
        };

        public static object Run(string code, string side, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            var key = Hash(code);

            var method = cache.GetOrAdd(key, _ => Compile(code));
            var compileMs = sw.ElapsedMilliseconds;

            sw.Restart();
            var value = Dispatch.OnSide<object>(side, () =>
            {
                try
                {
                    return method.Invoke(null, new object[] { Hub.Sapi, Hub.Capi });
                }
                catch (TargetInvocationException tie) when (tie.InnerException != null)
                {
                    // Report what the snippet threw, not "Exception has been thrown
                    // by the target of an invocation."
                    throw tie.InnerException;
                }
            }, timeoutMs);
            var runMs = sw.ElapsedMilliseconds;

            return new
            {
                value = Json.Simplify(value),
                type = value?.GetType().FullName,
                side = side ?? "server",
                compileMs,
                runMs,
                cached = compileMs < 5
            };
        }

        /// <summary>
        /// Snippets come in two shapes and we do not make the caller declare which.
        /// A bare expression ("sapi.WorldManager.Seed") is tried first; if that
        /// does not compile, the same text is retried as a statement body. When
        /// both fail the statement-form diagnostics are reported, since multi-line
        /// input is almost always the intent behind a real failure.
        /// </summary>
        static MethodInfo Compile(string code)
        {
            if (TryCompile(Wrap(code, expression: true), out var m, out _)) return m;
            if (TryCompile(Wrap(code, expression: false), out m, out var errors)) return m;

            throw new VerbException("compile failed:\n" + string.Join("\n", errors), "compile_error");
        }

        static string Wrap(string code, bool expression)
        {
            var usings = string.Join("\n", DefaultUsings.Select(u => $"using {u};"));
            // A single-expression snippet written without a trailing semicolon is
            // the common interactive case. Supplying it here keeps the statement
            // attempt from failing with a misleading "; expected" that hides the
            // real error.
            var statements = code.TrimEnd();
            if (statements.Length > 0 && !statements.EndsWith(";") && !statements.EndsWith("}"))
                statements += ";";

            var body = expression
                ? $"return (object)({code});"
                : statements + "\nreturn null;";

            return $@"
{usings}

public static class VstkSnippet
{{
    public static object Run(Vintagestory.API.Server.ICoreServerAPI sapi,
                             Vintagestory.API.Client.ICoreClientAPI capi)
    {{
{body}
    }}
}}";
        }

        static bool TryCompile(string source, out MethodInfo method, out List<string> errors)
        {
            method = null;
            errors = new List<string>();

            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "VstkSnippet_" + Guid.NewGuid().ToString("N"),
                new[] { tree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));

            using var ms = new MemoryStream();
            EmitResult emit = compilation.Emit(ms);

            if (!emit.Success)
            {
                errors = emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => Describe(d, source))
                    .ToList();
                return false;
            }

            ms.Seek(0, SeekOrigin.Begin);
            var asm = Assembly.Load(ms.ToArray());
            method = asm.GetType("VstkSnippet").GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            return method != null;
        }

        /// <summary>
        /// Reports diagnostics against the snippet as the caller wrote it, not
        /// against the generated wrapper, whose line numbers would be meaningless.
        /// </summary>
        static string Describe(Diagnostic d, string source)
        {
            var span = d.Location.GetLineSpan();
            var lineNo = span.StartLinePosition.Line;
            var lines = source.Split('\n');
            var text = lineNo >= 0 && lineNo < lines.Length ? lines[lineNo].Trim() : "";
            return $"{d.Id}: {d.GetMessage()}" + (text.Length > 0 ? $"\n    at: {text}" : "");
        }

        /// <summary>
        /// Every loaded assembly with a real file location. This is what puts the
        /// mod under test in scope without anyone configuring anything.
        /// </summary>
        internal static IEnumerable<MetadataReference> References()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var refs = new List<MetadataReference>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string location;
                try
                {
                    if (asm.IsDynamic) continue;
                    location = asm.Location;
                }
                catch { continue; }

                if (string.IsNullOrEmpty(location)) continue;
                if (!seen.Add(location)) continue;

                try { refs.Add(MetadataReference.CreateFromFile(location)); }
                catch { }
            }

            return refs;
        }

        static string Hash(string s)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }
    }
}
