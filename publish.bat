@echo off
echo Publishing CS2Launcher...

REM 获取最新的 Git commit ID
for /f "delims=" %%i in ('git rev-parse --short HEAD') do set COMMIT_ID=%%i
echo Current commit ID: %COMMIT_ID%

REM 创建发布目录
if not exist "./publish" mkdir "./publish"

REM 发布 Windows 64 位不包含 runtime 的版本
echo Publishing for Windows 64-bit without runtime...
dotnet publish CS2Launcher.csproj -c Release -o ./publish/win-x64-standalone -r win-x64

REM 发布 Windows 32 位不包含 runtime 的版本
echo Publishing for Windows 32-bit without runtime...
dotnet publish CS2Launcher.csproj -c Release -o ./publish/win-x86-standalone -r win-x86

REM 发布 Windows 64 位包含 runtime 的版本
echo Publishing for Windows 64-bit with runtime...
dotnet publish CS2Launcher.csproj -c Release -o ./publish/win-x64-with-runtime --self-contained -r win-x64

REM 发布 Windows 32 位包含 runtime 的版本
echo Publishing for Windows 32-bit with runtime...
dotnet publish CS2Launcher.csproj -c Release -o ./publish/win-x86-with-runtime --self-contained -r win-x86

echo Publishing completed!

REM 创建压缩包
echo Creating archives...
powershell -Command "Compress-Archive -Path './publish/win-x64-standalone/*' -DestinationPath './publish/CS2Launcher-win-x64-standalone-%COMMIT_ID%.zip' -Force"
powershell -Command "Compress-Archive -Path './publish/win-x86-standalone/*' -DestinationPath './publish/CS2Launcher-win-x86-standalone-%COMMIT_ID%.zip' -Force"
powershell -Command "Compress-Archive -Path './publish/win-x64-with-runtime/*' -DestinationPath './publish/CS2Launcher-win-x64-with-runtime-%COMMIT_ID%.zip' -Force"
powershell -Command "Compress-Archive -Path './publish/win-x86-with-runtime/*' -DestinationPath './publish/CS2Launcher-win-x86-with-runtime-%COMMIT_ID%.zip' -Force"

echo Archives created successfully!
echo.
echo Published versions:
echo - Windows 64-bit without runtime: ./publish/win-x64-standalone/
echo - Windows 32-bit without runtime: ./publish/win-x86-standalone/
echo - Windows 64-bit with runtime: ./publish/win-x64-with-runtime/
echo - Windows 32-bit with runtime: ./publish/win-x86-with-runtime/
echo.
echo ZIP archives:
echo - ./publish/CS2Launcher-win-x64-standalone-%COMMIT_ID%.zip (requires .NET runtime)
echo - ./publish/CS2Launcher-win-x86-standalone-%COMMIT_ID%.zip (requires .NET runtime)
echo - ./publish/CS2Launcher-win-x64-with-runtime-%COMMIT_ID%.zip (includes .NET runtime)
echo - ./publish/CS2Launcher-win-x86-with-runtime-%COMMIT_ID%.zip (includes .NET runtime)
