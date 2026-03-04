#!/bin/bash
set -e
echo "Publishing CS2Launcher..."

COMMIT_ID="$(git rev-parse --short HEAD 2>/dev/null || echo local)"
echo "Current commit ID: ${COMMIT_ID}"

mkdir -p ./publish

COMMON_PROPS=(-p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true)

echo "Publishing for Windows x64 (self-contained, single-file)..."
dotnet publish CS2Launcher.csproj -c Release -r win-x64 --self-contained -o ./publish/win-x64-with-runtime "${COMMON_PROPS[@]}"

echo "Creating zip..."
(cd ./publish && zip -r "CS2Launcher-win-x64-with-runtime-${COMMIT_ID}.zip" win-x64-with-runtime >/dev/null)

echo "Publish completed successfully!"
echo "Published files are in the ./publish directory"