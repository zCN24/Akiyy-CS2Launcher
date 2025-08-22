#!/bin/bash
echo "Building CS2Launcher..."
dotnet build CS2Launcher.csproj -c Release -p:Platform=x64
if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi
echo "Build completed successfully!"