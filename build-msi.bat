@echo off
setlocal enabledelayedexpansion

set ROOT=%~dp0
set CONFIG=Release
set RID=win-x64
set APP_PUBLISH_DIR=%ROOT%artifacts\publish\app
set UPDATER_PUBLISH_DIR=%ROOT%artifacts\publish\updater
set MSI_OUT_DIR=%ROOT%artifacts\installer
set INSTALLER_PROJECT=%ROOT%installer\CS2Launcher.Installer.wixproj
set APP_PROJECT=%ROOT%CS2Launcher.csproj
set UPDATER_PROJECT=%ROOT%CS2Launcher.Updater\CS2Launcher.Updater.csproj
set MSI_NAME=CS2Launcher-%VERSION%-win-x64-with-runtime.msi

if not "%~1"=="" (
  set VERSION=%~1
) else (
  set VERSION=1.0.0
)
set MSI_NAME=CS2Launcher-%VERSION%-win-x64-with-runtime.msi

echo [1/5] Clean artifacts...
if exist "%APP_PUBLISH_DIR%" rmdir /s /q "%APP_PUBLISH_DIR%"
if exist "%UPDATER_PUBLISH_DIR%" rmdir /s /q "%UPDATER_PUBLISH_DIR%"
if exist "%MSI_OUT_DIR%" rmdir /s /q "%MSI_OUT_DIR%"
mkdir "%APP_PUBLISH_DIR%"
mkdir "%UPDATER_PUBLISH_DIR%"
mkdir "%MSI_OUT_DIR%"

echo [2/5] Publish app (self-contained .NET 8)...
dotnet publish "%APP_PROJECT%" -c %CONFIG% -r %RID% --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o "%APP_PUBLISH_DIR%"
if errorlevel 1 goto :failed

echo [3/5] Publish updater (self-contained single-file)...
dotnet publish "%UPDATER_PROJECT%" -c %CONFIG% -r %RID% --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:EnableCompressionInSingleFile=true -o "%UPDATER_PUBLISH_DIR%"
if errorlevel 1 goto :failed

copy /y "%UPDATER_PUBLISH_DIR%\Updater.exe" "%APP_PUBLISH_DIR%\Updater.exe" >nul
if errorlevel 1 goto :failed

echo [4/5] Build MSI...
dotnet build "%INSTALLER_PROJECT%" -c %CONFIG% -p:PublishDir="%APP_PUBLISH_DIR%" -p:Version=%VERSION% -p:OutputPath="%MSI_OUT_DIR%\"
if errorlevel 1 goto :failed

set GENERATED_MSI=
for %%F in ("%MSI_OUT_DIR%\*.msi") do (
  set GENERATED_MSI=%%~fF
  goto :found_msi
)

echo No MSI generated.
goto :failed

:found_msi
if /I not "%GENERATED_MSI%"=="%MSI_OUT_DIR%\%MSI_NAME%" (
  if exist "%MSI_OUT_DIR%\%MSI_NAME%" del /f /q "%MSI_OUT_DIR%\%MSI_NAME%"
  ren "%GENERATED_MSI%" "%MSI_NAME%"
  if errorlevel 1 goto :failed
)

echo [5/5] Done.
echo MSI output: %MSI_OUT_DIR%\%MSI_NAME%
exit /b 0

:failed
echo Build MSI failed.
exit /b 1
