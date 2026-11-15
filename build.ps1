param([switch]$Test, [switch]$UiTest)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sdkDirectory = Join-Path $projectRoot '.tools\dotnet-10.0.404'
$dotnet = Join-Path $sdkDirectory 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { & "$projectRoot\tools\bootstrap_dotnet.ps1" }
$env:DOTNET_ROOT = $sdkDirectory
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$outputDirectory = Join-Path $projectRoot 'release_v3_21'
& $dotnet publish "$projectRoot\AirportSignStudio.csproj" -c Release -r win-x64 --self-contained true -o $outputDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'Single-file publication failed.' }
$files = @(Get-ChildItem -LiteralPath $outputDirectory -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'AirportSignStudio.exe') { throw 'Expected exactly one portable executable.' }
Write-Output "Built standalone $outputDirectory\AirportSignStudio.exe"
if ($Test -or $UiTest) {
    $validation = Join-Path $projectRoot 'validation_v3_21'
    New-Item -ItemType Directory -Path $validation -Force | Out-Null
    & $dotnet build "$projectRoot\tests\Verify.Modern.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Verification compilation failed.' }
    $tests = Join-Path $projectRoot 'tests\bin\Release\net10.0-windows\Verify.Modern.dll'
    if ($Test) {
        & $dotnet $tests $validation
        if ($LASTEXITCODE -ne 0) { throw 'Artwork/export verification failed.' }
    }
    if ($UiTest) {
        & $dotnet $tests --ui $validation
        if ($LASTEXITCODE -ne 0) { throw 'UI verification failed.' }
    }
}
