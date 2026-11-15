$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$checkRoot = Join-Path $projectRoot 'validation_v3_21\portable_smoke'
$appDirectory = Join-Path $checkRoot '仅一个文件 portable'
$cacheDirectory = Join-Path $checkRoot 'runtime-cache'
$emptyRuntime = Join-Path $checkRoot 'empty-runtime'
New-Item -ItemType Directory -Path $appDirectory, $cacheDirectory, $emptyRuntime -Force | Out-Null
$source = Join-Path $projectRoot 'release_v3_21\AirportSignStudio.exe'
$executable = Join-Path $appDirectory '机场标志工坊.exe'
Copy-Item -LiteralPath $source -Destination $executable -Force
if (@(Get-ChildItem -LiteralPath $appDirectory -Force).Count -ne 1) { throw 'Smoke directory must contain only the executable.' }
if (@(Get-ChildItem -LiteralPath $emptyRuntime -Force).Count -ne 0) { throw 'Runtime override must remain empty.' }
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class PortableSmokeWindows {
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
    public static Dictionary<IntPtr,string> ForProcess(int id) {
        var found = new Dictionary<IntPtr,string>();
        EnumWindows((h,p) => { uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner == id) { var title = new StringBuilder(512); GetWindowText(h,title,title.Capacity); found[h]=title.ToString(); }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.WorkingDirectory = $appDirectory
$start.Environment['DOTNET_ROOT'] = $emptyRuntime
$start.Environment['DOTNET_ROOT_X64'] = $emptyRuntime
$start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $cacheDirectory
$traceFile = Join-Path $checkRoot 'host-trace.txt'
Set-Content -LiteralPath $traceFile -Value '' -Encoding utf8
$start.Environment['DOTNET_HOST_TRACE'] = '1'
$start.Environment['DOTNET_HOST_TRACEFILE'] = $traceFile
$process = [Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForInputIdle(20000)) { throw 'App did not become ready.' }
    $ready = [Diagnostics.Stopwatch]::StartNew()
    do {
        $windows = [PortableSmokeWindows]::ForProcess($process.Id)
        $window = $windows.GetEnumerator() | Where-Object Value -eq '拼图编辑器 · 未命名工程 · Airport Sign Studio 3.21' | Select-Object -First 1
        if ($null -eq $window) { Start-Sleep -Milliseconds 100 }
    } while ($null -eq $window -and -not $process.HasExited -and $ready.ElapsedMilliseconds -lt 15000)
    if ($null -eq $window) { throw ('Main window was not created. Found: ' + ($windows.Values -join ', ')) }
    $perMonitorV2 = [PortableSmokeWindows]::AreDpiAwarenessContextsEqual([PortableSmokeWindows]::GetWindowDpiAwarenessContext($window.Key), [IntPtr](-4))
    if (-not $perMonitorV2) { throw 'Main window did not use PerMonitorV2.' }
    $process.Refresh()
    $modules = @($process.Modules | Where-Object ModuleName -in 'coreclr.dll','clrjit.dll' | ForEach-Object { $_.FileName })
    # Modern Windows singlefilehost statically links the runtime into the EXE.
    if ($modules.Count -ne 0) { throw ('Unexpected external runtime modules: ' + ($modules -join ', ')) }
    [PortableSmokeWindows]::PostMessage($window.Key, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    if (-not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'App did not close cleanly.' }
    $trace = Get-Content -LiteralPath $traceFile -Raw
    foreach ($evidence in @('Detected Single-File app bundle', 'Executing as a self-contained app', 'Using internal fxr', 'Using internal hostpolicy', "CoreCLR path = '', CoreCLR dir = ''")) {
        if (-not $trace.Contains($evidence)) { throw "Missing bundled-runtime evidence: $evidence" }
    }
    $files = @(Get-ChildItem -LiteralPath $appDirectory -Force)
    if ($files.Count -ne 1) { throw 'App required or wrote adjacent files.' }
    [ordered]@{
        passed = $true; timestamp = (Get-Date).ToString('o'); exe = $executable
        sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
        bytes = (Get-Item -LiteralPath $executable).Length; adjacentFiles = @($files.Name)
        title = $window.Value; perMonitorV2 = $perMonitorV2; runtimeOverride = $emptyRuntime
        externalRuntimeModules = $modules; hostTrace = $traceFile; exitCode = $process.ExitCode
        limitation = 'Same Windows host; another physical PC and real monitor transitions have not been tested.'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checkRoot 'portable-check.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $checkRoot 'portable-check.json')
} finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
