---
name: csharp-solid
description: >
  C# structure for HeroesReplay: SOLID, small ports, and .NET coding standards.
  Use when adding or changing C# types, interfaces, DI registration, or /csharp-solid.
---

# C# structure

Formatting, the `net10.0-windows` TFM, CSharpier, and test commands live in `dotnet-10-csharpier`. This skill is how a change is shaped.

Do not vendor these. Read them when the change is a behavior-preserving refactor or a public API:

- [.NET team `csharp-refactoring`](https://github.com/dotnet/skills/tree/main/plugins/dotnet/skills/csharp-refactoring) (MIT, in `dotnet/skills`)
- [Framework design guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/)
- [C# coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)

## SOLID in this repo

- One reason to change. `Spectator` orchestrates a replay. It does not own pixels or the clock formula.
- A new way to read time or capture a frame is a new class behind the existing port (`IGameTimer`, `IGameCapture`), registered in `ServiceCollectionExtensions`. Do not add that method to `Spectator` or `GameController`.
- Ports stay small. `IGameTimer` is read and reset. `IGameCapture` is the frame and the client size. Do not merge them.
- A stub implements the same port and returns an empty result. It does not throw because a dependency is missing.
- Callers depend on the port. The composition root picks `PrintWindowCapture`, `BitBltCapture`, or `StubCapture`.

## Standards

- Match the file you are editing. Nullable is off in `Directory.Build.props`. File-scoped namespaces. `ConfigureAwait(false)` on library awaits. Async methods take a `CancellationToken` and the name ends with `Async`.
- Constructor-inject dependencies. No service locator. No static mutable state.
- Immutable results are records (`GameTimerReading`). Do not hide a second implementation inside a class whose name says something else.
- Log the decision (source, reason). A fallback is an explicit branch, not an empty catch.
- A refactor preserves behavior. Build and run the unit tests for the types you moved. Do not mix a behavior change into that step.
- Keep a name that JSON, DI, or config binds. Renaming a settings key is a behavior change.
