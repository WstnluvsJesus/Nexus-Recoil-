@echo off
setlocal
rem Run from a Visual Studio 2022 x64 Native Tools Command Prompt.
set "NEXUS_ROOT=%~dp0..\.."
cmake -S "%~dp0." -B "%~dp0obj" -G "Visual Studio 17 2022" -A x64
if errorlevel 1 exit /b 1
cmake --build "%~dp0obj" --config Release
if errorlevel 1 exit /b 1
dotnet publish "%NEXUS_ROOT%\source\Nexus-Recoil\Nexus-Recoil.csproj" -c Release
if errorlevel 1 exit /b 1
set "NEXUS_PUBLISH=%NEXUS_ROOT%\source\Nexus-Recoil\bin\Release\net8.0-windows\win-x64\publish"
copy /Y "%~dp0bin\Release\NexusVdeviceBridge.dll" "%NEXUS_PUBLISH%\NexusVdeviceBridge.dll" >nul
if errorlevel 1 exit /b 1
copy /Y "%~dp0vendor\LICENSE" "%NEXUS_PUBLISH%\Vdevice-SDK-LICENSE.txt" >nul
if errorlevel 1 exit /b 1
echo Published app and Vdevice bridge to "%NEXUS_PUBLISH%".
echo Run NexusRecoil.exe --vdevice from an administrator command prompt.
