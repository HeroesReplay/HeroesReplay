# Builds obs/interfaces/AhliObs 0.75 Blind.StormInterface, the interface a Blind Rating replay
# loads (#411), from AhliObs 0.75.StormInterface. It is the same interface with two of its own
# settings changed: hero names instead of player names above the health bars
# (HeroUnitStatusNameType=Unit) and the chat hidden (ChatInitiallyHidden=true).
#
# It saves the archive the way Ahli's Observer UI Settings Editor does (the editor wrote
# AhliObs 0.75.StormInterface, commit 2d0caf2): MPQEditor.exe extracts every file, the <Constant>
# values in _Config.StormLayout change and nothing else, and MPQEditor.exe builds a new archive
# with its Heroes compression preset (GameId=11 in %APPDATA%\MPQEditor.ini, put back afterwards),
# then compacts it. The result is read back: every other file is byte-identical and the two
# constants hold the new values.
#
# MPQEditor.exe ships with the Settings Editor
# (https://github.com/Ahli/Galaxy-Observer-UI/releases). Windows only. Run it again when
# AhliObs 0.75.StormInterface changes, and commit both files.
param(
    [string]$Source = '',
    [string]$Destination = '',
    [string]$MpqEditor = 'C:\Program Files\Observer UI Settings Editor\plugins\mpq\MPQEditor.exe'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $Source) {
    $Source = Join-Path $root 'obs\interfaces\AhliObs 0.75.StormInterface'
}

if (-not $Destination) {
    $Destination = Join-Path $root 'obs\interfaces\AhliObs 0.75 Blind.StormInterface'
}

$Source = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Source)
$Destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)

# The settings the Settings Editor lists for AhliObs (@Setting comments in _Config.StormLayout).
$settings = [ordered]@{
    HeroUnitStatusNameType = 'Unit'
    ChatInitiallyHidden = 'true'
}

if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    throw "No interface at $Source"
}

if (-not (Test-Path -LiteralPath $MpqEditor -PathType Leaf)) {
    throw "No MPQEditor.exe at $MpqEditor. Install the Observer UI Settings Editor or pass -MpqEditor."
}

function Invoke-MpqEditor([string[]]$Arguments) {
    $process = Start-Process -FilePath $MpqEditor -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "MPQEditor.exe $($Arguments -join ' ') exited $($process.ExitCode)"
    }
}

function Expand-Interface([string]$Archive, [string]$Folder) {
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    Invoke-MpqEditor @('e', "`"$Archive`"", '"*"', "`"$Folder`"", '/fp')
    foreach ($special in '(listfile)', '(attributes)') {
        $path = Join-Path $Folder $special
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    $files = @(Get-ChildItem -LiteralPath $Folder -Recurse -File)
    if ($files.Count -eq 0) {
        throw "MPQEditor.exe extracted nothing from $Archive"
    }

    return $files
}

function Get-Constant([string]$Text, [string]$Name) {
    $match = [regex]::Match($Text, '<Constant name="' + [regex]::Escape($Name) + '" val="([^"]*)"/>')
    if (-not $match.Success) {
        return $null
    }

    return $match.Groups[1].Value
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('heroesreplay-blind-' + [System.IO.Path]::GetRandomFileName())
$unpacked = Join-Path $work 'source'
$check = Join-Path $work 'check'
$built = Join-Path $work 'AhliObs 0.75 Blind.StormInterface'
$ini = Join-Path $env:APPDATA 'MPQEditor.ini'
$iniBackup = Join-Path $work 'MPQEditor.ini'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$hadIni = Test-Path -LiteralPath $ini
try {
    if ($hadIni) {
        Copy-Item -LiteralPath $ini -Destination $iniBackup
        $lines = @(Get-Content -LiteralPath $ini)
        if ($lines -match '^GameId=') {
            $lines = $lines -replace '^GameId=.*$', 'GameId=11'
        }
        else {
            $lines = $lines -replace '^\[Options\]$', "[Options]`r`nGameId=11"
        }

        Set-Content -LiteralPath $ini -Value $lines
    }
    else {
        Set-Content -LiteralPath $ini -Value @('[Options]', 'GameId=11')
    }

    $files = Expand-Interface $Source $unpacked
    $config = Join-Path $unpacked '_Config.StormLayout'
    if (-not (Test-Path -LiteralPath $config)) {
        throw "$Source has no _Config.StormLayout"
    }

    $text = [System.IO.File]::ReadAllText($config)
    foreach ($setting in $settings.GetEnumerator()) {
        $before = Get-Constant $text $setting.Key
        if ($null -eq $before) {
            throw "_Config.StormLayout has no constant $($setting.Key)"
        }

        $pattern = '(<Constant name="' + [regex]::Escape($setting.Key) + '" val=")[^"]*("/>)'
        $text = [regex]::Replace($text, $pattern, '${1}' + $setting.Value + '${2}')
        Write-Host "$($setting.Key): $before -> $($setting.Value)"
    }

    [System.IO.File]::WriteAllText($config, $text, [System.Text.UTF8Encoding]::new($false))

    # As the Settings Editor: room for (listfile) and (attributes), then add, then compact.
    Invoke-MpqEditor @('n', "`"$built`"", [string]($files.Count + 2))
    Invoke-MpqEditor @('a', "`"$built`"", "`"$unpacked`"", '""', '/r')
    Invoke-MpqEditor @('compact', "`"$built`"")
}
finally {
    if ($hadIni) {
        Copy-Item -LiteralPath $iniBackup -Destination $ini -Force
    }
    else {
        Remove-Item -LiteralPath $ini -Force -ErrorAction SilentlyContinue
    }
}

try {
    $rebuilt = Expand-Interface $built $check
    if ($rebuilt.Count -ne $files.Count) {
        throw "The new archive holds $($rebuilt.Count) files; the source holds $($files.Count)."
    }

    foreach ($file in $files) {
        $relative = $file.FullName.Substring($unpacked.Length)
        $copy = Join-Path $check $relative
        if (-not (Test-Path -LiteralPath $copy)) {
            throw "The new archive is missing $relative"
        }

        if ($relative -eq '\_Config.StormLayout') {
            continue
        }

        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) {
            throw "The new archive changed $relative"
        }
    }

    $written = [System.IO.File]::ReadAllText((Join-Path $check '_Config.StormLayout'))
    foreach ($setting in $settings.GetEnumerator()) {
        $value = Get-Constant $written $setting.Key
        if ($value -ne $setting.Value) {
            throw "The new archive has $($setting.Key)=$value, not $($setting.Value)"
        }
    }

    Copy-Item -LiteralPath $built -Destination $Destination -Force
    Write-Host "Wrote $Destination ($((Get-Item -LiteralPath $Destination).Length) bytes, $($files.Count) files)."
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
