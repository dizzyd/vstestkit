using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace VsTestkit
{
    /// <summary>
    /// Compiles a suite from .cs files inside the game process.
    ///
    /// The game already carries Roslyn to build source mods, so a machine running
    /// tests needs no .NET SDK - which matters because Cairn provisions a runtime,
    /// not an SDK, and a headless test box is exactly where you least want to
    /// install a toolchain.
    ///
    /// You still author against a normal csproj on a workstation and get full
    /// compile-time checking there; this is how the sources get built where they
    /// run. References come from whatever is loaded, so the mod under test is in
    /// scope without configuration - the same rule /eval follows.
    /// </summary>
    public static class SourceSuite
    {
        public static byte[] Compile(IReadOnlyList<string> files, out string label)
        {
            if (files.Count == 0) throw new VerbException("no .cs files to compile", "no_sources");

            label = files.Count == 1
                ? files[0]
                : $"{Path.GetDirectoryName(files[0])} ({files.Count} files)";

            var trees = files.Select(f =>
                CSharpSyntaxTree.ParseText(
                    File.ReadAllText(f),
                    path: f,
                    encoding: Encoding.UTF8)).ToList();

            var compilation = CSharpCompilation.Create(
                "VstkSuite_" + Guid.NewGuid().ToString("N"),
                trees,
                Evaluator.References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Debug));

            using var ms = new MemoryStream();
            var emit = compilation.Emit(ms);

            if (!emit.Success)
            {
                var errors = emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(Describe)
                    .Take(25)
                    .ToList();

                throw new VerbException(
                    $"compile failed ({errors.Count} error(s)):\n" + string.Join("\n", errors),
                    "compile_error");
            }

            return ms.ToArray();
        }

        /// <summary>Every .cs file under a directory, or the single file given.</summary>
        public static List<string> Discover(string path)
        {
            if (File.Exists(path)) return new List<string> { Path.GetFullPath(path) };

            if (!Directory.Exists(path))
                throw new VerbException($"no such file or directory: {path}", "no_sources");

            // bin/ and obj/ hold build output; compiling those would duplicate
            // every type in the suite.
            return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Select(Path.GetFullPath)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>file:line: message, so an error points at the source you wrote.</summary>
        static string Describe(Diagnostic d)
        {
            var span = d.Location.GetLineSpan();
            var file = string.IsNullOrEmpty(span.Path) ? "?" : Path.GetFileName(span.Path);
            return $"{file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): " +
                   $"{d.Id}: {d.GetMessage()}";
        }
    }
}
