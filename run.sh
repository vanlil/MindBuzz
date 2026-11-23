#!/usr/bin/env bash
# Builds and starts MindBuzz (Release) with a new, empty document. run.cmd is the same for Windows.
set -euo pipefail
cd "$(dirname "$0")"
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
exec dotnet run --project src/Desktop -c Release
