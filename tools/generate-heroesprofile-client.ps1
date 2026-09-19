# Regenerates src/HeroesReplay.HeroesProfile.Client/Generated from the public OpenAPI spec.
# Does not vendor the 5MB JSON. Requires the local microsoft.openapi.kiota tool.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot/..

dotnet tool restore | Out-Null

$spec = 'https://raw.githubusercontent.com/Heroes-Profile/heroesprofile/refs/heads/develop/public/spec/heroesprofile-v1.json'
$out = 'src/HeroesReplay.HeroesProfile.Client/Generated'

dotnet kiota generate `
    -l CSharp `
    -c HeroesProfileClient `
    -n HeroesReplay.HeroesProfile.Client `
    -d $spec `
    -o $out `
    --include-path /replays `
    --include-path /replays/** `
    --include-path /download/replay `
    --include-path '/replay/{replayID}' `
    --exclude-backward-compatible `
    --clean-output
