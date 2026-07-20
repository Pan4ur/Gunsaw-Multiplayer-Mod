$ErrorActionPreference = "Stop"
dotnet build .\GunsawMultiplayer.csproj -c Release
Write-Host "Built: bin\Release\netstandard2.0\GunsawMultiplayer.dll"
