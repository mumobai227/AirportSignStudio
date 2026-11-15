param([switch]$Test, [switch]$UiTest, [string]$OcrRedistPath)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sdkDirectory = Join-Path $projectRoot '.tools\dotnet-10.0.404'
$dotnet = Join-Path $sdkDirectory 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { & "$projectRoot\tools\bootstrap_dotnet.ps1" }
$env:DOTNET_ROOT = $sdkDirectory
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$crtNames = @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll', 'msvcp140_1.dll')
if (-not $OcrRedistPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $redistDirs = foreach ($installation in (& $vswhere -products '*' -property installationPath)) {
            $folder = Join-Path $installation 'VC\Redist\MSVC'
            if (-not (Test-Path -LiteralPath $folder)) { continue }
            foreach ($version in (Get-ChildItem -LiteralPath $folder -Directory)) {
                $number = $null
                if (-not [version]::TryParse($version.Name, [ref]$number)) { continue }
                $x64 = Join-Path $version.FullName 'x64'
                if (-not (Test-Path -LiteralPath $x64)) { continue }
                foreach ($crt in (Get-ChildItem -LiteralPath $x64 -Directory -Filter 'Microsoft.VC*.CRT')) {
                    if (@($crtNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $crt.FullName $_) -PathType Leaf) }).Count -eq 0) {
                        [pscustomobject]@{ Version = $number; Directory = $crt.FullName }
                    }
                }
            }
        }
        $OcrRedistPath = $redistDirs | Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty Directory
    }
}
if (-not $OcrRedistPath) { throw 'Install the Visual Studio x64 C++ redistributable build component or provide its app-local CRT folder with -OcrRedistPath.' }
foreach ($name in $crtNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $OcrRedistPath $name) -PathType Leaf)) { throw "OCR redistributable file missing: $name" }
}
$outputDirectory = Join-Path $projectRoot 'release_v3_26_0'
& $dotnet publish "$projectRoot\AirportSignStudio.csproj" -c Release -r win-x64 --self-contained true -o $outputDirectory --nologo "-p:OcrRedistPath=$OcrRedistPath"
if ($LASTEXITCODE -ne 0) { throw 'Single-file publication failed.' }
$files = @(Get-ChildItem -LiteralPath $outputDirectory -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'AirportSignStudio.exe') { throw 'Expected exactly one portable executable.' }
Write-Output "Built standalone $outputDirectory\AirportSignStudio.exe"
if ($Test -or $UiTest) {
    $validation = Join-Path $projectRoot 'validation_v3_26_0'
    New-Item -ItemType Directory -Path $validation -Force | Out-Null
    & $dotnet build "$projectRoot\tests\Verify.Modern.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Verification compilation failed.' }
    $tests = Join-Path $projectRoot 'tests\bin\Release\net10.0-windows10.0.19041.0\Verify.Modern.dll'
    if ($Test) {
        & $dotnet $tests $validation
        if ($LASTEXITCODE -ne 0) { throw 'Artwork/export verification failed.' }
    }
    if ($UiTest) {
        & $dotnet $tests --ui $validation
        if ($LASTEXITCODE -ne 0) { throw 'UI verification failed.' }
    }
}
