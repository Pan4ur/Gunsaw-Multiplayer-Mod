#!/usr/bin/env sh
set -eu
dotnet build ./GunsawMultiplayer.csproj -c Release
echo "Built: bin/Release/netstandard2.0/GunsawMultiplayer.dll"
