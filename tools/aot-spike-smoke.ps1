# Native AOT spike only (not for merge). Runs a few offline commands against one build and
# writes a markdown table: exit code, time, and the first lines of stdout/stderr.
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Label,
    [Parameter(Mandatory)] [string] $Replay,
    [Parameter(Mandatory)] [string] $OutDir
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Invoke-Case([string] $Name, [string[]] $Arguments, [string[]] $StdinLines = $null, [int] $TimeoutSec = 180) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $Exe
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    if ($StdinLines) {
        try {
            foreach ($l in $StdinLines) { $p.StandardInput.WriteLine($l); $p.StandardInput.Flush(); Start-Sleep -Milliseconds 1500 }
            Start-Sleep -Seconds 3
        } catch { Write-Warning "$Name stdin: $($_.Exception.Message)" }
    }
    try { $p.StandardInput.Close() } catch { }
    $exited = $p.WaitForExit($TimeoutSec * 1000)
    if (-not $exited) { $p.Kill($true); $p.WaitForExit() }
    $sw.Stop()
    $file = Join-Path $OutDir ("$Label-" + ($Name -replace '[^A-Za-z0-9]+', '-') + '.log')
    "### $Label $Name`nexit=$($p.ExitCode) timedOut=$(-not $exited) ms=$($sw.ElapsedMilliseconds)`n--- stdout`n$($out.Result)`n--- stderr`n$($err.Result)" | Set-Content $file
    $firstErr = ($err.Result -split "`n" | Where-Object { $_ -match 'Exception|error|fail' } | Select-Object -First 1)
    [pscustomobject]@{
        Build = $Label; Case = $Name; Exit = $(if ($exited) { $p.ExitCode } else { 'timeout' }); Ms = $sw.ElapsedMilliseconds
        StdoutBytes = $out.Result.Length; FirstError = ($firstErr ?? '').Trim() -replace '\|', '/'
    }
}

$init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"aot-smoke","version":"0"}}}'
$mcp = @(
    $init,
    '{"jsonrpc":"2.0","method":"notifications/initialized"}',
    '{"jsonrpc":"2.0","id":2,"method":"tools/list"}',
    '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_spectator_status","arguments":{}}}',
    '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"check_config","arguments":{}}}'
)

$rows = @(
    Invoke-Case 'help' @('--help')
    Invoke-Case 'version' @('--version')
    Invoke-Case 'config-effective-json' @('config', 'effective', '--output', 'json')
    Invoke-Case 'config-effective-obs' @('config', 'effective', '--section', 'OBS')
    Invoke-Case 'check-config-json' @('check', 'config', '--output', 'json')
    Invoke-Case 'obs-bundle-json' @('obs', 'bundle', '--output', 'json')
    Invoke-Case 'services-status-json' @('services', 'status', '--output', 'json')
    Invoke-Case 'focus-coverage' @('focus', 'coverage')
    Invoke-Case 'calculators-coordinates' @('calculators', 'coordinates', '--file', $Replay) -TimeoutSec 600
    # Loads the heroes-data2 catalog (Heroes.Element) and the embedded talent-build reference.
    Invoke-Case 'builds-match' @('builds', 'match', '--replay', $Replay) -TimeoutSec 600
    Invoke-Case 'mcp-stdio' @('mcp') -StdinLines $mcp -TimeoutSec 60
)
$rows | Export-Csv (Join-Path $OutDir "$Label-summary.csv") -NoTypeInformation
$md = "| Build | Case | Exit | ms | stdout bytes | first error line |`n|---|---|---|---|---|---|`n"
$md += ($rows | ForEach-Object { "| $($_.Build) | $($_.Case) | $($_.Exit) | $($_.Ms) | $($_.StdoutBytes) | $($_.FirstError.Substring(0, [Math]::Min(160, $_.FirstError.Length))) |" }) -join "`n"
$md | Set-Content (Join-Path $OutDir "$Label-summary.md")
$md
