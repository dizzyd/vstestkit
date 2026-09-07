import json
import os
from pathlib import Path
import shlex
import shutil
import unittest

from support import Sandbox


class TargetPathTests(unittest.TestCase):
    def test_sdk_and_imported_output_paths_reach_tests_load(self):
        with Sandbox() as box:
            boot = box.run("boot.sh")
            self.assertEqual(boot.returncode, 0, boot.stdout + boot.stderr)
            commands = box.base / "commands"
            commands.mkdir()
            dotnet = commands / "dotnet"
            # The fixture has no real mod binary to build. Suite builds and
            # MSBuild property evaluation still use the actual SDK.
            dotnet.write_text('''#!/usr/bin/env bash
if [ "$1" = build ] && [ "$2" = "$FIXTURE_ROOT/VsTestkit/VsTestkit.csproj" ]; then exit 0; fi
exec ''' + shlex.quote(shutil.which("dotnet")) + ' "$@"\n')
            dotnet.chmod(0o755)
            env = {"PATH": str(commands) + os.pathsep + os.environ["PATH"], "FIXTURE_ROOT": str(box.root)}
            project = box.base / "suite"
            project.mkdir()
            (project / "Suite.cs").write_text("public class Suite {}\n")
            csproj = project / "Suite.csproj"
            csproj.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
            for imported in (False, True):
                with self.subTest(imported=imported):
                    if imported:
                        (project / "custom.props").write_text('''<Project><PropertyGroup Condition="'$(Configuration)' == 'Debug'">
<AssemblyName>Renamed</AssemblyName><OutputPath>custom/$(Configuration)/</OutputPath>
</PropertyGroup></Project>''')
                        csproj.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project="custom.props" /></Project>')
                    result = box.run("run.sh", str(csproj), **env)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                    expected = project / ("custom/Debug/net10.0/Renamed.dll" if imported else "bin/Debug/net10.0/Suite.dll")
                    self.assertTrue(expected.is_file())
                    loaded = json.loads((box.run_dir / "data/last-load.json").read_text())
                    self.assertEqual(loaded["path"], str(expected))


if __name__ == "__main__":
    unittest.main()
