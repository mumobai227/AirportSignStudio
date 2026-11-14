$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $projectRoot '.tools'
$sdkDirectory = Join-Path $toolsDirectory 'dotnet-10.0.404'
$archive = Join-Path $toolsDirectory 'dotnet-sdk-10.0.404-win-x64.zip'
$expectedHash = '330ddd2665b43cd2e428216a0d8ea1ac7bb19b5434fdc530e62010dc3b52823a620bf95489d628e1ed6f5122401793ee4887cca4a019122361a1231497f632ee'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-win-x64.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'SDK SHA512 mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath $sdkDirectory -Force
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& (Join-Path $sdkDirectory 'dotnet.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'SDK preparation failed.' }
