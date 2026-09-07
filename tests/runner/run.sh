#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$ROOT/fixtures/Fixtures.csproj" -v quiet --nologo
dotnet run --project "$ROOT/RunnerChecks.csproj" --no-build -- "$ROOT/fixtures/bin/Debug/net10.0/Fixtures.dll"
for kind in BAD_TEST BAD_BEFORE BAD_AFTER BAD_VALUE_TASK; do
    OUT="$ROOT/invalid/bin/$kind"
    dotnet build "$ROOT/invalid/Invalid.csproj" -p:DefineConstants="$kind" -o "$OUT" -v quiet --nologo
    dotnet "$ROOT/bin/Debug/net10.0/RunnerChecks.dll" "$ROOT/fixtures/bin/Debug/net10.0/Fixtures.dll" "$OUT/Invalid.dll"
done
