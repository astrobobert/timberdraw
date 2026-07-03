# TimberDraw

**TimberDraw** is an AutoCAD plugin for drawing traditional timber frame bent geometry. It generates 2D elevation drawings and 3D solid models of full timber frame bents — including posts, rafters, girts, braces, struts, king posts, queen posts, and hammer beams — with accurate joinery (tenons, mortises, and drawbored pegs) conforming to Timber Framers Guild (TFG) standards.

TimberDraw is the first application in the **Timber Frame Suite**.

---

## Features

- **Five bent types:** King Post, Queen Post, Hammer Beam, King Post Truss, Queen Post Truss
- **2D and 3D modes:** Flat elevation for layout, or fully extruded 3D solid model
- **Complete joinery generation:** Tenons, mortises, and pegs drawn per Timber Framers Guild standards
  - Peg diameter = 1/2 x tenon thickness (TFG rule)
  - First peg 2" from bearing nose; subsequent pegs at 3.5x peg-diameter spacing
  - Far-end clearance matches near-end: 2" minimum from tenon tip (TFG symmetric setback)
  - Number of pegs placed dynamically — only pegs that fit within the tenon bounds are drawn
- **Bay framing:** Common rafters, purlins, floor girts, and bay braces between bents
- **AutoCAD palette UI:** All parameters entered in a docked tool palette; no command-line prompts
- **Persistent settings:** All bent parameters save between sessions via AutoCAD's settings system
- **Project file save/load:** `TSave` writes `BentProject.json` capturing all bent and bay parameters; `TLoad` restores them and refreshes the palette
- **Parametric regeneration:** `TRegenTimber` command edits any timber's Width/Depth/JointType in place; automatically cascades to re-cut mortises in all connected members and updates the selection set of the legacy TimberTag palette

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
2. Copy `..\bin\Debug\TimberDraw.dll` (build output lands one level above the repo root) to a convenient location.
3. In AutoCAD, type `NETLOAD` and browse to `TimberDraw.dll`.
4. Type `TDraw` to open the TimberDraw palette.

To load automatically on startup, add the `NETLOAD` path to your AutoCAD startup suite (`APPLOAD` -> Startup Suite).

---

## Usage

Open the palette with the `TDraw` command. Configure your bent:

| Setting | Description |
|---|---|
| **Bent Type** | King Post, Queen Post, Hammer Beam, King Post Truss, Queen Post Truss |
| **Span** | Overall bent width (outside of post to outside of post) |
| **Eave Height** | Height from grade to the top of the wall plate / eave line |
| **Roof Pitch** | Rise and run (e.g. 8/12) |
| **Bay Width** | Spacing between bents (for 3D and bay member generation) |
| **Member sizes** | Width and depth for each member type |
| **Has Joinery** | Generate tenons, mortises, and pegs |
| **Has Shoulder** | Add housing shoulders to beam seats |
| **Make 3D** | Extrude all members to full 3D solids |
| **Bent Number / Wall** | Roman-numeral bent label and wall letter for designations |

Click the draw button for the appropriate bent type. The plugin places the bent at the AutoCAD insertion point using the current UCS.

---

## Project Structure

```
TimberDraw\
  Commands.cs          -- AutoCAD command registration (TDraw command)
  Module1.cs           -- Global state and core draw helpers (DrawElement, DrawPeg, AddJoint)
  UserControl1.cs      -- Windows Forms palette UI
  Commons.cs           -- Common rafters between bents

  PostLeft.cs          -- Left wall post
  PostRight.cs         -- Right wall post
  BentGirt.cs          -- Tie beam / bent girt
  BentBrace.cs         -- Knee brace
  BayBrace.cs          -- Bay brace (between bents)
  EaveGirt.cs          -- Eave girt
  FloorBentGirt.cs     -- Floor girt in bent plane
  FloorBayGirt.cs      -- Floor girt across bays
  TrussGirt.cs         -- Girt for truss bents
  Ridge.cs             -- Ridge beam
  Purlins.cs           -- Roof purlins
  ProjectFile.cs       -- TSave / TLoad commands (BentProject.json read/write)

  Kpost\               -- King Post bent components
    KPBent.cs          -- Orchestrates the full King Post bent
    KPost.cs           -- King post
    KPRafterLeft.cs    -- Left principal rafter
    KPRafterRight.cs   -- Right principal rafter
    KPStrutLeft.cs     -- Left diagonal strut
    KPStrutRight.cs    -- Right diagonal strut
    KPVertStrutLeft.cs -- Left vertical strut
    KPVertStrutRight.cs

  Qpost\               -- Queen Post bent components (same pattern as Kpost\)
  Hbeam\               -- Hammer Beam bent components
  KpostTruss\          -- King Post Truss variant
  QpostTruss\          -- Queen Post Truss variant

  Pegs\                -- Timber Framers Guild peg standards
    PegSpecification.cs     -- Peg data model (namespace: TimberFrameSuite.Standards)
    TFGPegStandards.cs      -- Standard presets and lookup (namespace: TimberFrameSuite.Standards)
    JoineryExporter.cs      -- JSON export stub for future TimberScribe app
    INTEGRATION_GUIDE.md    -- Guide for wiring into TimberScribe
```

---

## Timber Framers Guild Peg Standards

All pegs are sized and placed per TFG (Timber Frame Engineering Council) research:

**Peg sizing:**
- Peg diameter = 1/2 x tenon thickness
- 2" tenon -> 1" diameter peg (most members)
- 1.5" tenon -> 0.75" diameter peg (braces)
- Maximum standard peg diameter: 1.25"

**Peg placement along beam:**
- First peg: 2" from bearing nose (framing-square body thickness)
- Spacing between pegs: 3.5 x peg diameter
- Far-end clearance: 2" from tenon tip (symmetric with near-end setback)
- Peg count: determined dynamically — only pegs that fit within [2", Depth - 2"] are drawn
  - Typical 6-8" members: 1-2 pegs per joint
  - Members 11"+ deep: up to 3 pegs per joint

**References:**
- *Edge Spacing of Pegs in Mortise and Tenon Joints* -- TFEC Research Report
- *Structural Properties of Pegged Timber Connections as Affected by End Distance* -- TFEC
- Timber Framers Guild: https://www.tfguild.org

---

## Timber Frame Suite

TimberDraw is the drawing engine of a planned three-app suite:

| App | Status | Purpose |
|---|---|---|
| **TimberDraw** (this project) | Active development | AutoCAD drawing plugin — draws managed timber solids, cuts joinery, labels/BOM/shop maps, exports `.tsj` scribe files (`TScribe`/`TScribeAll`) |
| **TimberScribe** | Active development | Raspberry Pi Flask server — receives `.tsj` files and drives laser print head |
| **TimberTag** | **Legacy — superseded** | Old AutoCAD tagging/export plugin; its scribe export, BOM, and tagging roles have been absorbed into TimberDraw. Only needed for drawings made by the legacy parametric pipeline. |

The `Pegs\` folder contains `JoineryExporter.cs`, a pre-staged stub originally planned for a TimberScribe AutoCAD plugin. TimberScribe is now a standalone Pi application and consumes `.tsj` files produced by TimberDraw's managed scribe export (`Managed\Scribe*.cs`, ported from the original TimberTag pipeline); `JoineryExporter.cs` is retained for reference only.

---

## Development

### Building

Open `TimberDraw.sln` in Visual Studio 2022 and build, or from PowerShell:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TimberDraw.csproj /p:Configuration=Debug /nologo /v:minimal
```

Output: `..\bin\Debug\TimberDraw.dll` (one level above the repo root)

### Key Technical Details

- **SDK-style `.csproj`** -- all `.cs` files in the project directory tree are compiled automatically; no need to add files manually.
- **C# 9.0** -- required for `new()` target-typed creation used in `Pegs\` classes.
- **Source encoding** -- all source files must contain only ASCII characters. The .NET Framework compiler reads files in the system ANSI codepage; Unicode decorative characters in source will cause compile errors.
- **ObjectARX SDK** -- DLLs referenced by absolute path from `C:\Autodesk\ObjectARX_for_AutoCAD_2020_Win_64_bit\inc\`.
- **AutoCAD command** -- `TDraw` opens the palette. The command name cannot match the assembly name (`TimberDraw`) due to AutoCAD namespace conflict.

### Adding a New Member Type

1. Create a class in the appropriate subdirectory following the pattern in any existing member class.
2. Declare `private double TenonWidth = 2;` (or `1.5` for braces).
3. In the joinery block, call `TFGPegStandards.GetPresetForTenonThickness(TenonWidth)` to compute `r`, `y1/y2/y3`, and `maxPegPos`. Use the bounds check pattern:
   ```csharp
   double maxPegPos = tenonLength - peg.FirstPegSetbackInches;
   PegCol.Add(DrawPeg at y1);               // always
   if (y2 <= maxPegPos) PegCol.Add(...y2);  // conditional
   if (y3 <= maxPegPos) PegCol.Add(...y3);  // conditional
   ```
4. Add `using TimberFrameSuite.Standards;` at the top of the file.
5. Wire the new class into the appropriate `*Bent.cs` orchestrator.

---

## History

Originally written in VB.NET around 2009. Converted to C# and modernized to an SDK-style project in 2026. Renamed from DrawAFrame to TimberDraw, May 2026. TFG peg standards integration and dynamic peg bounds enforcement completed May 2026. Phase 2 parametric regeneration (all member types) and Phase 3 receiver-driven mortise re-cut + cascade handle correction + TimberTag SS update completed May 2026.

---

## License

MIT -- see [LICENSE](LICENSE).
