# Repository Guidelines

## Project Structure & Module Organization

- `src/LightSync.Core/` contains vendor-neutral audio analysis, capture, colour processing, zone mapping, and pipeline code. It must not reference device adapters.
- `src/LightSync.Application/` contains the shared `DeviceAdapterFactory` and device profile persistence.
- `src/LightSync.Cli/` contains command-line entry points. `run` defaults to audio; screen sync uses `run --source screen`.
- `src/LightSync.Desktop/` contains the Avalonia interface for device management, audio sync, exact RGB tests, and master brightness control.
- `src/LightSync.Devices.<Vendor>/` holds one adapter project per lighting vendor. Nanoleaf is implemented; Hue, WLED, and OpenRGB are placeholders.
- `tests/` mirrors production projects with xUnit v3 test projects. `docs/` contains setup, architecture, development, and protocol notes.

## Build, Test, and Development Commands

Run these from the repository root with the .NET 10 SDK selected by `global.json`:

```bash
dotnet restore
dotnet build -c Release --no-restore   # must finish with zero warnings
dotnet test -c Release --no-build      # unit and integration tests
dotnet publish src/LightSync.Cli -c Release -o out
./out/light-sync --help
```

GStreamer-backed tests skip when `gst-launch-1.0` is unavailable. Install GStreamer to exercise them locally, as CI does.

## Coding Style & Naming Conventions

Follow `.editorconfig`: four spaces for C#, two for Markdown, JSON, and project files; UTF-8, LF line endings, and a final newline. Use file-scoped namespaces, `System`-first usings outside namespaces, braces, nullable reference types, and PascalCase for public types and members. Keep hot-path work allocation-free: prefer pre-sized buffers and `Span<T>`; do not add LINQ to frame processing.

Warnings are errors (`AnalysisMode=All`). Make a justified project-wide analyzer exception in `.editorconfig`, never an inline suppression. Source code is trim/AOT-safe: avoid reflection and add serialised types to the appropriate source-generated JSON context.

## Testing Guidelines

Name test files `*Tests.cs` and test observable behaviour, including exact protocol bytes where relevant. Keep tests machine-independent: do not read real credentials or depend on local hardware. Prefer recorded real command output for parsers. Run `dotnet test` before opening a pull request.

## Commit & Pull Request Guidelines

Use short imperative commit subjects, e.g. `Add Nanoleaf discovery tests` or `Fix partial configuration defaults`. Keep commits focused. Pull requests should explain the behavioural change, link relevant issues, include test results, and add screenshots or CLI output when user-facing behaviour changes. Never commit tokens, generated `out/` binaries, or local configuration files.
