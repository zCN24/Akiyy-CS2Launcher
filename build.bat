@echo off
echo Building CS2Launcher...
dotnet build CS2Launcher.csproj -c Release -p:Platform=x64
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b %ERRORLEVEL%
)
echo Build completed successfully!