@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "PUBLISH_EXIT=1"
set "PUBLISH_PAUSE=1"
if /i "%~1"=="--help" goto help
if /i "%~1"=="--no-pause" (
    set "PUBLISH_PAUSE=0"
) else if not "%~1"=="" (
    echo Unknown argument: %~1
    goto help
)

pushd "%~dp0"
if errorlevel 1 goto finish

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: Install the .NET 10 SDK, then run this script again.
    goto cleanup
)

set "PUBLISH_OUTPUT=%~dp0publish"

echo Publishing SnapSharp - Release / win-x64 / self-contained
echo Output: "%PUBLISH_OUTPUT%"
echo Model weights and local user settings are not included.
echo.

dotnet publish "%~dp0SnapSharp.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_OUTPUT%" -m:1 -p:UseSharedCompilation=false -p:PublishSingleFile=false -p:PublishTrimmed=false
if errorlevel 1 (
    echo.
    echo ERROR: Publish failed. Review the output above.
    goto cleanup
)

copy /y "%~dp0LICENSE" "%PUBLISH_OUTPUT%\LICENSE" >nul
if errorlevel 1 goto copy_failed
copy /y "%~dp0THIRD-PARTY-NOTICES.md" "%PUBLISH_OUTPUT%\THIRD-PARTY-NOTICES.md" >nul
if errorlevel 1 goto copy_failed
copy /y "%~dp0README.md" "%PUBLISH_OUTPUT%\README.md" >nul
if errorlevel 1 goto copy_failed
if not exist "%PUBLISH_OUTPUT%\SnapSharp.exe" (
    echo ERROR: Publish did not produce SnapSharp.exe.
    goto cleanup
)

echo.
echo SUCCESS: "%PUBLISH_OUTPUT%\SnapSharp.exe"
echo Distribute the entire output directory, not only the EXE.
echo The target PC does not need the .NET Desktop Runtime.
echo Native features may still require VC++ x64 Runtime / Media Foundation.
set "PUBLISH_EXIT=0"
goto cleanup

:copy_failed
echo ERROR: Could not copy distribution documentation.
goto cleanup

:help
echo Usage: publish.bat [--no-pause ^| --help]
echo Publishes a self-contained Windows x64 Release build to:
echo   publish\ (next to this script)
echo --no-pause: return immediately after publishing, for command-line use.
if /i "%~1"=="--help" exit /b 0
exit /b 1

:cleanup
popd
:finish
if "%PUBLISH_PAUSE%"=="1" pause
exit /b %PUBLISH_EXIT%
