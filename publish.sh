#!/bin/bash
echo "Publishing CS2Launcher..."
dotnet publish CS2Launcher.csproj -c Release -p:Platform=x64 -o ./publish --self-contained true
if [ $? -ne 0 ]; then
    echo "Publish failed!"
    exit 1
fi
echo "Publish completed successfully!"
echo "Published files are in the ./publish directory"