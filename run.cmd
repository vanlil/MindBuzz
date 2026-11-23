@echo off
rem Builds and starts MindBuzz (Release) with a new, empty document. run.sh is the same for macOS and Linux.
cd /d "%~dp0"
set AVALONIA_TELEMETRY_OPTOUT=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
dotnet run --project src\Desktop -c Release
