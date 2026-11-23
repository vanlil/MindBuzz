@echo off
rem MindBuzz build script for Windows; build.sh is its twin for macOS and Linux, with the same options.
rem It spells out the dotnet commands so nobody retypes them, and adds no build logic: every project still builds with
rem a plain `dotnet build`.
rem
rem   build                       restore, build Release, run all tests
rem   build debug                 the same in Debug
rem   build --no-tests            build only (also -n)
rem   build --publish [rid]       the release build for this machine, or for rid: single-file, self-contained, trimmed
rem   build --mac                 release builds for osx-arm64 and osx-x64, each with MindBuzz.app
rem   build --windows             release build for win-x64
rem   build --linux               release builds for linux-x64 and linux-arm64
rem   build --aot                 NativeAOT for this machine, for measuring only
rem   build --clean [--list]      delete every bin\ obj\ artifacts\ (or only list them)
rem   build --help                this text (also -h, --?)
rem
rem Release builds go to artifacts\<rid>\, NativeAOT to artifacts\<rid>-aot\.

setlocal EnableDelayedExpansion
cd /d "%~dp0"

rem No build-time telemetry: the app makes no network calls, and the build should not either.
set AVALONIA_TELEMETRY_OPTOUT=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set TESTINGPLATFORM_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1

set configuration=Release
set tests=1
set rids=
set aot=0
set hostrid=win-x64
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set hostrid=win-arm64

:args
if "%~1"=="" goto build
if /i "%~1"=="-h" goto help
if /i "%~1"=="--help" goto help
if "%~1"=="--?" goto help
if /i "%~1"=="debug" (set configuration=Debug& shift & goto args)
if /i "%~1"=="-n" (set tests=0& shift & goto args)
if /i "%~1"=="--no-tests" (set tests=0& shift & goto args)
if /i "%~1"=="--mac" (set rids=!rids! osx-arm64 osx-x64& shift & goto args)
if /i "%~1"=="--windows" (set rids=!rids! win-x64& shift & goto args)
if /i "%~1"=="--linux" (set rids=!rids! linux-x64 linux-arm64& shift & goto args)
if /i "%~1"=="--aot" (set aot=1& shift & goto args)
if /i "%~1"=="--clean" goto clean
if /i "%~1"=="--publish" goto publisharg
echo Unknown option: %~1 (see build --help) 1>&2
exit /b 2

:publisharg
set next=%~2
if "%next%"=="" (set rids=!rids! %hostrid%& shift & goto args)
if "%next:~0,1%"=="-" (set rids=!rids! %hostrid%& shift & goto args)
set rids=!rids! %next%
shift & shift & goto args

:build
call :run dotnet build MindBuzz.slnx -c %configuration% || exit /b 1
rem One project at a time, each with the fewest tests it may run: a broken test setup (a fixture or static field
rem failing before the tests start) drops whole classes, and dotnet test still says "Passed!". Raise the numbers
rem when the suites grow a lot.
if "%tests%"=="1" (
  call :run dotnet test --project tests\Core.Tests -c %configuration% --no-build --minimum-expected-tests 240 || exit /b 1
  call :run dotnet test --project tests\App.Tests -c %configuration% --no-build --minimum-expected-tests 470 || exit /b 1
)

rem The release shape. Publishing for osx-* also writes MindBuzz.app (src\Desktop\MacBundle.targets).
for %%r in (%rids%) do (
  call :run dotnet publish src\Desktop\MindBuzz.Desktop.csproj -c Release -r %%r -o artifacts\%%r -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true || exit /b 1
)

if "%aot%"=="1" (
  call :run dotnet publish src\Desktop\MindBuzz.Desktop.csproj -c Release -r %hostrid% -o artifacts\%hostrid%-aot -p:PublishAot=true -p:SelfContained=true || exit /b 1
)
exit /b 0

:clean
rem Only the folders the build creates, never anything under .git; with --list, only shown.
for /d /r %%d in (bin obj artifacts) do (
  if exist "%%d" (
    echo %%d | findstr /i /c:"\.git\\" >nul || (
      if /i "%~2"=="--list" (echo %%d) else (rd /s /q "%%d" && echo deleted %%d)
    )
  )
)
exit /b 0

:help
for /f "usebackq skip=1 tokens=* delims=" %%l in ("%~f0") do (
  set line=%%l
  if not "!line:~0,3!"=="rem" goto :eof
  echo(!line:~4!
)
exit /b 0

:run
echo ^> %*
%*
exit /b %errorlevel%
