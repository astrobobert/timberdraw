# Contributing to TimberDraw

Thanks for your interest! TimberDraw is an AutoCAD plugin, which makes the
contribution surface a little unusual: **building and running the plugin needs
AutoCAD + the ObjectARX SDK, but a lot of valuable work doesn't.**

## Two contribution paths

### Without AutoCAD

You can contribute meaningfully with nothing but a .NET 8 SDK and a text
editor:

- **Documentation** — the [user guide](docs/user-guide/README.md) (chapters
  need review and figures), [GLOSSARY.md](GLOSSARY.md) (trade-term accuracy),
  README/CLAUDE.md.
- **Architecture tests** — `dotnet test tests/TimberDraw.Architecture.Tests`
  runs anywhere: the tests read the production sources as text and reference
  neither AutoCAD nor the plugin assembly. This is also what CI runs.
- **Design discussion** — issues about joinery geometry, labeling conventions,
  and shop workflow are welcome from working framers especially.

### With AutoCAD (building the plugin)

| Requirement | Notes |
|---|---|
| Windows, Visual Studio 2022 | Community edition is fine |
| .NET Framework 4.8 targeting pack | |
| AutoCAD 2020 | The verified host |
| ObjectARX SDK for AutoCAD 2020 | Free from Autodesk; the project references it by **absolute path** at `C:\Autodesk\ObjectARX_for_AutoCAD_2020_Win_64_bit\` — install it there or adjust the references locally (don't commit path changes) |

Build:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TimberDraw.csproj /p:Configuration=Debug /nologo /v:minimal
```

Output currently lands at `..\bin\Debug\TimberDraw.dll` — **one level above
the repo root** (a deliberate leftover for a stable APPLOAD path; see the
README). `NETLOAD` it in AutoCAD; `TVer` confirms the loaded build. AutoCAD
locks the DLL — close AutoCAD before rebuilding.

## Ground rules

- **ASCII-only in `.cs` files** — comments and strings included. The net48
  compiler reads sources in the ANSI codepage; any Unicode character breaks
  the build with CS1056. (`.md` files may use Unicode.)
- **Respect the generator/editor boundary.** Generator code (`Frame\`, the
  bent folders) and editor code (`Managed\`) must not reference each other's
  types; only `Frame\ManagedFrameEmitter.cs` and `Frame\FrameGrid.cs` may
  cross. The architecture tests enforce this — when you add a folder or a
  core type, extend the token lists in `BoundaryTests.cs`.
- **Naming comes from GLOSSARY.md.** Joinery parameters use the canonical
  names (Seat, ShoulderTop/Bottom, Thickness, Offset, Length). If a concept
  isn't in the glossary, propose the term there first.
- **The `.tsj` schema changes in lockstep** with its parser — see
  [TSJ_SPEC.md](https://github.com/astrobobert/timberscribe/blob/master/TSJ_SPEC.md).
- Colorblind-safe UI/output: never green as an indicator; blue vs yellow/red.

## Pull requests

- Keep PRs focused; describe what you exercised (built + loaded in AutoCAD,
  or arch-tests-only — say which).
- `dotnet test tests/TimberDraw.Architecture.Tests` must pass.
- New commands/parameters need a line in the user guide's
  [command reference](docs/user-guide/appendix-a-commands.md) and, if they
  introduce vocabulary, a GLOSSARY entry.
