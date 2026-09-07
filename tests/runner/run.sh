#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$ROOT/fixtures/Fixtures.csproj" -v quiet --nologo
dotnet run --project "$ROOT/RunnerChecks.csproj" --no-build -- "$ROOT/fixtures/bin/Debug/net10.0/Fixtures.dll"
