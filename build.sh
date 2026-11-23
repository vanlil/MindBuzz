#!/usr/bin/env bash
# MindBuzz build script for macOS and Linux; build.cmd is its twin for Windows, with the same options.
# It spells out the dotnet commands so nobody retypes them, and adds no build logic: every project still builds with
# a plain `dotnet build`.
#
#   ./build.sh                       restore, build Release, run all tests
#   ./build.sh debug                 the same in Debug
#   ./build.sh --no-tests            build only (also -n)
#   ./build.sh --publish [rid]       the release build for this machine, or for rid: single-file, self-contained,
# trimmed
#   ./build.sh --mac                 release builds for osx-arm64 and osx-x64, each with MindBuzz.app
#   ./build.sh --windows             release build for win-x64
#   ./build.sh --linux               release builds for linux-x64 and linux-arm64
#   ./build.sh --aot                 NativeAOT for this machine, for measuring only
#   ./build.sh --clean [--list]      delete every bin/ obj/ artifacts/ (or only list them)
#   ./build.sh --help                this text (also -h, --?)
#
# Release builds go to artifacts/<rid>/, NativeAOT to artifacts/<rid>-aot/.

set -euo pipefail
cd "$(dirname "$0")"

# No build-time telemetry: the app makes no network calls, and the build should not either.
export AVALONIA_TELEMETRY_OPTOUT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 TESTINGPLATFORM_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

configuration=Release
tests=1
rids=()
aot=0

host_rid() {
  local os arch
  case "$(uname -s)" in Darwin) os=osx ;; *) os=linux ;; esac
  case "$(uname -m)" in arm64 | aarch64) arch=arm64 ;; *) arch=x64 ;; esac
  echo "$os-$arch"
}

run() {
  echo "> $*"
  "$@"
}

while [ $# -gt 0 ]; do
  case "$1" in
    -h | --help | --\?) sed -n '2,/^$/p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    debug) configuration=Debug ;;
    -n | --no-tests) tests=0 ;;
    --publish)
      if [ $# -gt 1 ] && [ "${2#-}" = "$2" ]; then rids+=("$2"); shift; else rids+=("$(host_rid)"); fi ;;
    --mac) rids+=(osx-arm64 osx-x64) ;;
    --windows) rids+=(win-x64) ;;
    --linux) rids+=(linux-x64 linux-arm64) ;;
    --aot) aot=1 ;;
    --clean)
      # Only the folders the build creates, never anything under .git; with --list, only shown.
      dirs=$(find . -path ./.git -prune -o -type d \( -name bin -o -name obj -o -name artifacts \) -prune -print | sort)
      for dir in $dirs; do
        if [ "${2:-}" = "--list" ]; then echo "$dir"; else rm -rf "$dir"; echo "deleted $dir"; fi
      done
      exit 0 ;;
    *) echo "Unknown option: $1 (see ./build.sh --help)" >&2; exit 2 ;;
  esac
  shift
done

run dotnet build MindBuzz.slnx -c "$configuration"

# One project at a time, each with the fewest tests it may run: a broken test setup (a fixture or static field
# failing before the tests start) drops whole classes, and dotnet test still says "Passed!". Raise the numbers
# when the suites grow a lot.
if [ "$tests" = 1 ]; then
  run dotnet test --project tests/Core.Tests -c "$configuration" --no-build --minimum-expected-tests 240
  run dotnet test --project tests/App.Tests -c "$configuration" --no-build --minimum-expected-tests 470
fi

# The release shape. Publishing for osx-* also writes MindBuzz.app (src/Desktop/MacBundle.targets).
for rid in ${rids[@]+"${rids[@]}"}; do
  run dotnet publish src/Desktop/MindBuzz.Desktop.csproj -c Release -r "$rid" -o "artifacts/$rid" \
    -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true \
    -p:EnableCompressionInSingleFile=true
done

if [ "$aot" = 1 ]; then
  rid="$(host_rid)"
  run dotnet publish src/Desktop/MindBuzz.Desktop.csproj -c Release -r "$rid" -o "artifacts/$rid-aot" \
    -p:PublishAot=true -p:SelfContained=true
fi
