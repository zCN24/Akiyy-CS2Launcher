@echo off
echo Publishing CS2Launcher...
dotnet publish CS2Launcher.csproj -c Release -p:Platform=x64 -o ./publish --self-contained true
if %ERRORLEVEL% NEQ 0 (
    echo Publish failed!
    exit /b %ERRORLEVEL%
)
echo Publish completed successfully!
echo Published files are in the ./publish directory