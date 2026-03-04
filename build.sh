#!/bin/bash
set -e
echo "Building CS2Launcher (Release, x64)..."

echo "Restoring packages..."
dotnet restore CS2Launcher.csproj

dotnet build CS2Launcher.csproj -c Release -p:Platform=x64 --no-restore
echo "Build completed successfully!"