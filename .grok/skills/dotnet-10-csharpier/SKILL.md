---
name: dotnet-10-csharpier
description: >
  .NET 10 LTS, heroes-replay.slnx, CSharpier, Windows TFM, test categories.
  Use when changing csproj, Directory.Build.*, formatting, tests, or /dotnet-10-csharpier.
---

# .NET 10 + CSharpier

## Layout

- Solution: repo-root `heroes-replay.slnx` only (no `.sln`).
- TFM: `net10.0-windows10.0.19041.0` for CLI, Core, Tests. `HeroesReplay.HeroesProfile.Client` is `net10.0` (Kiota, no Windows APIs).
- Versions: `Directory.Packages.props`. SDK features: `Directory.Build.props`.
- Format: local tool `csharpier` 1.3.x. Config `.csharpierrc.json`.

## Commands

```powershell
dotnet tool restore
dotnet csharpier format src
dotnet csharpier check src
dotnet build heroes-replay.slnx
dotnet test heroes-replay.slnx
dotnet test heroes-replay.slnx -p:TestCategory=Integration
dotnet test heroes-replay.slnx -p:TestCategory=Smoke
dotnet build heroes-replay.slnx -p:CSharpierCheck=true
```

## Style

- File-scoped namespaces; usings at file top.
- `using` declarations over nested `using` blocks when dispose scope is the method.
- Do not enable ImplicitUsings unless you strip redundant usings in the same change.
- After file-scoped conversion, `Polly.Context` collides with `HeroesReplay.Core.Services.Context`. Alias: `using PollyContext = Polly.Context`.

## Packages

Stay on .NET 10 LTS lines (`Microsoft.Extensions.*` 10.0.x). Skip CommandLine 3 / Extensions 11 prereleases. Polly stays 7.x until cache policies are rewritten.

## CLI

How to invoke `heroesreplay` (spectate, check, calculators): skill `heroes-replay-cli`. After a CLI change, run Smoke tests and the matching `check` subcommand.
