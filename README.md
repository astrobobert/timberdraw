# TimberDraw

**TimberDraw** is the design side of the **Timber Frame Suite** — an open-source CAD/CAM system for traditional timber framers. The suite helps you design timber frames, produce shop drawings and cut lists, and laser-scribe accurate joinery layouts directly onto the timbers. The joinery is still cut by hand using traditional tools, preserving craftsmanship while reducing layout time and improving accuracy.

TimberDraw is the AutoCAD half of that pipeline. It models the frame as managed 3D solids — a parametric rough-in of five classic bent types, per-timber editing, and mortise-and-tenon joinery conforming to Timber Framers Guild (TFG) practice — and produces everything the shop needs: structural-grid labels, a cut list / BOM, shop drawings, and `.tsj` scribe files for the companion **TimberScribe** laser print head.

---

## Features

- **Parametric frame design:** A tree editor (`TDraw`) models the frame as Bents x Walls x Bays with five bent types -- King Post, Queen Post, Hammer Beam, King Post Truss, Queen Post Truss -- plus bay framing (eave/floor girts, braces, ridge, common rafters, purlins) and recipe parameters that survive regeneration (girt drop, per-bay girt heights, rafter tails, and more).
- **Managed timbers:** Every solid carries its own geometry, joinery, and identity as XData -- there is no side database. A one-way freeze (`TFreeze`) ends the parametric phase; after that every timber is edited the same way, skeleton or free.
- **Editor verbs:** Place, span, join, fit, re-section, scarf-splice, and joist-fill commands (`TPlace`, `TSpan`, `TJoin`, `TFit`, `TSection`, `TScarf`, `TJoist`), driven from an assembly palette (`TPanel`) with sticky section sizes and UCS presets.
- **Joinery engine:** A kit-of-parts connection catalog -- girt-to-post tenon + housing + pegs (`TJoint`, batch `TJointAll`), braces and struts at any angle (`TBrace`, `TStrut`), rafter foot/head, ridge drop-in, birdsmouths, housed dovetail purlins, and scarfs. Joints are identity-tracked: re-cutting replaces in place, and every cutter has a matching delete command. Pegs follow TFG sizing and placement standards.
- **Structural grid + labels:** Bent numbers and wall letters (skipping I/O) address every timber; labels are type-first (`P-2A`, `EG-B-I`, `J-A-1`) with hand and level qualifiers. Each timber carries a location label, a cut-mark shared by identical sticks (the buy list), and a stable production number.
- **Shop output:** `TBom` (sortable cut-list grid with model highlighting and CSV export), `TShop` (per-bent, per-wall, and floor-plan shop maps in paper-space layouts), and `TScribe`/`TScribeAll` (`.tsj` burn-path files per timber face for the TimberScribe laser head).
- **Frame browser:** `TBrowse` lists the whole frame for assign-and-review -- selecting highlights, double-click zooms.

The shared vocabulary (timber anatomy, joint types, canonical parameter names) lives in [GLOSSARY.md](GLOSSARY.md).

---

## Requirements

| Requirement | Version |
|---|---|
| AutoCAD | 2020 or compatible |
| .NET Framework | 4.8 |
| Visual Studio | 2022 (for building) |
| ObjectARX SDK | AutoCAD 2020 Win 64-bit |

---

## Installation

1. Build the project in Visual Studio 2022 (Debug or Release).
2. The build output is `bin\Debug\TimberDraw.dll` in the repo root; copy it to a convenient location (or load it in place).
3. In AutoCAD, type `NETLOAD` and browse to `TimberDraw.dll`.
4. Type `TDraw` to open the frame editor.

To load automatically on startup, add the `NETLOAD` path to your AutoCAD startup suite (`APPLOAD` -> Startup Suite). `TVer` confirms which build is loaded; NETLOAD cannot hot-swap, so reopen AutoCAD to pick up a new DLL.

---

## Workflow

1. **Design** -- `TDraw` opens the tree editor: add bents and bays, pick bent types, set spans, heights, pitch, member sizes, and roof framing. Draw regenerates the skeleton until you freeze.
2. **Freeze** -- `TFreeze` ends the parametric phase (one-way). The timbers carry on as the source of truth.
3. **Edit** -- `TPanel` opens the assembly palette; use the editor verbs to add floors, infill, and free timbers. `TAssign` gives free timbers grid addresses; `TScan` re-derives connectivity from face coincidence.
4. **Joinery** -- `TJointAll` batch-cuts the girt-to-post joints; the per-connection commands (`TBrace`, `TStrut`, `TRafterFoot`, `TRidge`, `TPurlin`, ...) or the Joints pane (`TPanel` -> Joints) cut the rest.
5. **Output** -- `TBom` for the cut list, `TShop` for shop drawings, `TScribeAll` for `.tsj` laser scribe files consumed by TimberScribe.

---

## Project Structure

```
TimberDraw\
  Commands.cs            -- Command registration + palettes (TDraw, TRoughIn, TFreeze, TGrid, TPanel, TBrowse, TVer, ...)
  FrameTreeControl.cs    -- The frame tree editor UI (FrameSpec is the source of truth)
  PropertyPane.cs        -- Ordered property panel used by the tree editor
  TimberPaletteControl.cs-- Assembly palette (TPanel): sticky sections + verb buttons
  JoinPaletteControl.cs  -- Joints pane: connection type + element stack, live re-cut
  BomGridControl.cs      -- BOM palette grid (TBom)

  Frame\                 -- The GENERATOR + frame model
    FrameSpec.cs         -- The recipe (Bents x Walls x Bays), edited by the tree editor
    KingPostBentGraph.cs -- Bent geometry as convex intersections of half-planes
                            (+ QueenPost / HammerBeam / Truss partials)
    ManagedFrameEmitter.cs -- The freeze bridge: emits managed timber solids from the graph
    FrameGrid.cs         -- Structural grid + installer labels
    FrameRegistry.cs     -- The one-way freeze gate

  Managed\               -- The MANAGED SUBSTRATE (editor + joinery + output)
    ManagedTimber.*.cs   -- The core, six partial files: anchor (keys/layers/erase), Model
                            (TFrame/specs), Solid (build/draw/rebuild), Geometry (joint
                            builders/faces), Persist (xrecords), Query (enumeration/picking)
    *Commands.cs         -- The command shells (partial class ManagedCommands): Core (shared
                            spine), Place, Assign, Scarf, GirtPost, RafterRidge, RoofInfill,
                            StrutBrace, Scan, Joist, Adopt, Profile, Copy + JoinCommands (Joints
                            pane), Relabel, Bom, Shop, Scribe
    ShopMaps.cs / ShopLayouts.cs -- TShop shop drawings
    Scribe*.cs           -- TScribe / TScribeAll .tsj export + annotations

  Browser\               -- TBrowse frame browser palette
  Ui\                    -- Shared shell: tabbed palette (Shell), dialogs, theme, pane row idioms
  docs\                  -- floor-systems design doc, user guide
  GLOSSARY.md            -- Trade terms + canonical joinery parameter names (single source of truth)

  Pegs\                  -- TFG peg standards (future shared library)
  tests\                 -- Architecture boundary + domain characterization tests (standalone, not in the .sln)
```

The legacy parametric pipeline (`Bent\`, `Bay\`, `Kpost\`, `Joints\`, ...) was **removed** in July 2026 —
the managed path is the only pipeline. The old design docs live in git history (commit `1ad54d9`).

---

## Timber Framers Guild Peg Standards

All pegs are sized and placed per TFG (Timber Frame Engineering Council) research:

**Peg sizing:**
- Peg diameter = 1/2 x tenon thickness
- 2" tenon -> 1" diameter peg (most members)
- 1.5" tenon -> 0.75" diameter peg (braces)
- Maximum standard peg diameter: 1.25"

**Peg placement:**
- First peg: 2" from bearing nose (framing-square body thickness)
- Spacing between pegs: 3.5 x peg diameter
- Peg count: determined dynamically -- only pegs that fit within clearances are drawn
- On managed joints, pegs bore the post only; draw-boring the tenon is done by hand in the field

**References:**
- *Edge Spacing of Pegs in Mortise and Tenon Joints* -- TFEC Research Report
- *Structural Properties of Pegged Timber Connections as Affected by End Distance* -- TFEC
- Timber Framers Guild: https://www.tfguild.org

---

## Timber Frame Suite

| App | Status | Purpose |
|---|---|---|
| **TimberDraw** (this project) | Active development | AutoCAD plugin -- draws managed timber solids, cuts joinery, labels/BOM/shop drawings, exports `.tsj` scribe files |
| **[TimberScribe](https://github.com/astrobobert/timberscribe)** | Active development | Raspberry Pi Flask server -- receives `.tsj` files and drives the self-propelled laser print head |
| **TimberTag** | Legacy -- superseded | Old AutoCAD tagging/export plugin (archived with the pre-split monorepo); its scribe export, BOM, and tagging roles have been absorbed into TimberDraw. Only needed for drawings made by the legacy parametric pipeline. |

The `.tsj` schema is byte-compatible with the original TimberTag export, so the Pi does not care which produced the file.

---

## Development

### Building

Open `TimberDraw.sln` in Visual Studio 2022 and build, or from PowerShell:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TimberDraw.csproj /p:Configuration=Debug /nologo /v:minimal
```

Output: `bin\Debug\TimberDraw.dll` (in the repo root)

### Key Technical Details

- **SDK-style `.csproj`** -- all `.cs` files in the project directory tree are compiled automatically; no need to add files manually.
- **C# 9.0** -- required for `new()` target-typed creation.
- **Source encoding** -- all source files must contain only ASCII characters. The .NET Framework compiler reads files in the system ANSI codepage; Unicode decorative characters in source will cause compile errors.
- **ObjectARX SDK** -- DLLs referenced by absolute path from `C:\Autodesk\ObjectARX_for_AutoCAD_2020_Win_64_bit\inc\`.
- **AutoCAD command** -- `TDraw` opens the frame editor. The command name cannot match the assembly name (`TimberDraw`) due to AutoCAD namespace conflict.
- **Boundary guard** -- `tests\TimberDraw.Architecture.Tests` enforces the generator/editor folder split (run with `dotnet test`, standalone).

See [CLAUDE.md](CLAUDE.md) for the full architecture guide.

---

## History

Originally written in VB.NET around 2009. Converted to C# and modernized to an SDK-style project in 2026; renamed from DrawAFrame to TimberDraw, May 2026. The parametric regeneration/cascade pipeline (May 2026) was superseded in June 2026 by the managed-timber substrate: frame graph generator + one-way freeze, editor verbs, identity-tracked joinery engine, structural-grid labels, BOM/shop drawings, managed scribe export, and floor systems phase 1. Split out of the timber-frame-suite monorepo into its own repository, July 2026. The legacy parametric pipeline was deleted outright in the Phase C deep purge (2026-07-06), and a hardening pass followed (2026-07): characterization tests over the pure domain logic, session diagnostics for silently-swallowed failures, and a behavior-neutral decomposition of the managed-timber core file.

---

## License

MIT -- see [LICENSE](LICENSE).
