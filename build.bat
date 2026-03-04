@echo off
setlocal
echo Building CS2Launcher (Release, x64)...

echo Restoring packages...
dotnet restore CS2Launcher.csproj
if %ERRORLEVEL% NEQ 0 (
    echo Restore failed!
    exit /b %ERRORLEVEL%
)

dotnet build CS2Launcher.csproj -c Release -p:Platform=x64 --no-restore
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b %ERRORLEVEL%
)

echo Build completed successfully!
endlocal