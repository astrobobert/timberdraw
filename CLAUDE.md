# TimberDraw -- Claude Code Instructions

## What This Project Is

TimberDraw is a **.NET 4.8 AutoCAD plugin** (class library) that designs traditional timber frames as managed 3D solids in AutoCAD -- parametric rough-in, editor verbs, joinery cutting, structural-grid labels, BOM/shop drawings, and laser scribe export. It is the primary application of the **Timber Frame Suite**.

The plugin loads into AutoCAD via `NETLOAD`. `TDraw` opens the frame tree editor (the recipe/generator), `TPanel` opens the assembly palette with the managed editor verbs; the full command set is tabled under "Editor commands" below. Naming (trade terms + canonical joinery param names) is governed by `GLOSSARY.md` -- the single source of truth.

## Architecture Direction (READ FIRST -- 2026-06-16)

This supersedes the "parametric model is the live source of truth" framing in the sections below (Parametric Timber Model, Cross-Bent Mortise Wiring, the Phase 1-4 cascade/regen machinery). Those sections remain accurate as **the generator's internals**, but the model that governs NEW work is:

- **The timber is the source of truth.** Geometry and connectivity live on the managed timber itself -- its faces, and the nodes derived from face coincidence (TScan). Connectivity is which faces touch, not a role or a queue.
- **The grid is address only.** Bent lines (numbers) and wall lines (letters, skip I/O) locate and label timbers. The grid carries no connectivity and does not drive geometry once a timber is frozen; a timber's address is derived from where it sits relative to the lines, not the reverse.
- **The frame spec is a generator, not the model.** It is a recipe that emits skeleton timbers. It is authoritative only while it runs; at the break it freezes (seed params stored as XData on the frame group) and the timbers carry on as the truth.

Two surfaces, not two models:
- **Generator** (the parametric / FrameSpec path): edits the recipe, emits the skeleton. Owns per-member parametric edits + the cascade, **pre-break only**.
- **Editor** (the managed-timber path): TPlace / TSpan / TJoin / TScan / TAssign / TJoist / re-section / re-cut / move / delete. **Operates uniformly on every managed timber -- skeleton and free alike.** After the break the skeleton/free distinction disappears at the editor level.

Consequences for new work:
- The cross-bent mortise queues are **generation-time scaffolding** inside the generator's run, discarded at the freeze. Do not treat them as a persistent connectivity engine, and do not extend them to walls, floors, sub-walls, or partial sub-bents -- those are **free-assembly infill** (owner-anchored, face-coincidence), as floors already are via TJoist.
- The parametric/free boundary tracks **connectivity, not span or regularity.** Grid-anchored, full-module, role-implied -> generator/skeleton. Owner-anchored, face-coincidence, any span -> editor/free.
- Grid lines are a property of frame-spec bents (declaring a sub-bent emits its line; sub-bents take intermediate numbers like 1.1, parallel to intermediate wall lines A.1). Freely placed posts do NOT create grid lines; they are addressed by wall+bay+sequence and located by dimension.
- Shop drawings and the cut list are a **reporting layer that reads the model/XData** -- never re-derive geometry.

Naming and the labeling convention: see `GLOSSARY.md`; the superseded design docs live in git history (last complete at commit `1ad54d9`).

## Managed Substrate -- Editor + Joinery (the ACTIVE direction)

The managed-timber substrate is where new work happens; it lives in two folders. The legacy parametric
sections further below are the GENERATOR's internals only.

### Pipeline (recipe -> skeleton -> managed solids)

`Frame\` -- the generator + the managed model that feeds it:
- `FrameSpec.cs` -- the RECIPE (instance model: Bents x Walls x Bays, each carrying a `List<Timber>`),
  edited via the TDraw tree editor (`FrameTreeControl`). Geometry exposed as recipe params survives
  Generate (see `frame-recipe-parameter-expansion` memory: Girt Drop, per-bay girt Height, etc.).
- `KingPostBentGraph.Build(FrameSpec)` (+ QueenPost/HammerBeam/Truss partials) -- emits a `FrameGraph`
  whose edges are convex intersections of half-planes (role-agnostic bodies).
- `ManagedFrameEmitter.Emit` -- turns each FrameEdge into a managed timber solid + writes seat/scarf
  nodes; `FrameGrid` derives the installer labels. This is the FREEZE BRIDGE (the one generator file
  allowed to reference `Managed\`).
- `FrameRegistry` / `TFreeze` -- the one-way freeze gate. Once frozen the tree's Draw button refuses and
  the timbers carry on as the source of truth.
- **Regenerate is skeleton-only (2026-07-07):** `EraseFrame` keeps any FrameTag-matched timber whose
  `Free` xdata == "1" (stamped at every editor creation: DrawBox/DrawMiteredBrace/TScarf pieces/first
  TAssign of an unassigned timber) OR whose FloorTag is set (the generator never writes FloorTag) --
  hand-placed timbers survive a re-Generate, assigned or not. Joinery is DELAYED AND DELIBERATE
  (Robert's rule): nothing auto-cuts at place time; `TJointAll` (selection-scoped) cuts, `TJointSync`
  re-cuts after moves / re-attaches after a regen.

`Managed\` -- the managed timber model, the editor, and the output layer. `ManagedTimber.cs` is the core:
- `TFrame` struct -- a timber's WCS frame (`O`, axes `X/Y/Z`, `L/D/W`) + end-cut normals + feature lists.
  `Faces(TFrame)` returns the 6 nominal faces; `ComputeNodes` derives connectivity from face coincidence
  (`FacesMate`), surfaced by `TScan`. `BuildFramedSolid` builds the solid (box -> slice end cuts -> convex
  Cuts -> concave Subtracts -> joinery Features/Pegs/JointPolys/JointPolysZ); `DrawFramedSolid` /
  `RebuildFromFrame` persist everything as XData trailers and preserve the production `TagHandle`.

The rest of `Managed\`: jigs (`PlaceJig`/`SpanJig`/`ScarfJig`/`JigGeometry`), sticky section + assembly
model (`ManagedSection`/`ManagedAssembly`), connection types (`ConnectionType`/`JointDefaults`), the
Joints pane commands (`JoinCommands.cs`), labels (`RelabelCommands.cs`), BOM (`BomCommands.cs`), shop
drawings (`ShopCommands`/`ShopMaps`/`ShopLayouts`), and scribe export (`Scribe*.cs`). The frame browser
palette lives in `Browser\`.

### Editor commands

Frame lifecycle (`Commands.cs`): `TDraw` (tree editor palette), `TRoughIn` (generate from the spec),
`TFreeze` (one-way break), `TGrid` (redraw the structural grid), `TPanel` (assembly palette),
`TBrowse` (frame browser: assign + review surface; highlight on select, double-click zooms),
`TBom` (BOM grid palette), `TVer` (build stamp), `TFrameSave`/`TFrameLoad` (frame JSON).
RETIRED 2026-07-06 (Phase C, Robert's disposition): `TFrameFlat`, `TDrawLegacy`, `TRegenTimber`,
`TFrame`/`TFrameQP`/`TFrameHB`/`TFrameKPT`/`TFrameQPT`, `TFrameSave`/`TFrameLoad`, `TSave`/`TLoad` --
commands + their UI deleted (ProjectFile.cs, UserControl1, FrameControl, FrameStore). `TPickFace`
kept (debug). The legacy generator internals (`Bent\`/`Bay\`/`Kpost\`/..., `TimberFactory`,
`Joints\`) were DELETED in the same purge -- see "Legacy Parametric Pipeline -- REMOVED" below.

Editor verbs (`Managed\ManagedTimber.cs` unless noted):

| Command | Description |
|---|---|
| `TPlace` | Place one managed timber; pick the extrusion direction (no UCS re-roll per member). |
| `TSpan` | Connect two PICKED timbers -- finds the facing faces and fills the gap with a member. |
| `TJoin` | Connect two PICKED faces -- facing -> square-ended filler; angled -> mitered knee brace. |
| `TFit` | Trim/extend a timber's picked END onto a target face (square or mitered); other end stays. |
| `TSection` | Re-section a managed timber (change W x D) in place. |
| `TScarf` | Scarf-splice a timber into two pieces; stores the scarf interface node. |
| `TJoist` | Place a row of PLAIN floor joists in a wall/bay (Joist role, FLUSH tops, Drop). Joinery is DELIBERATE (Robert's rule): dovetails cut later via TJointAll's joist pass (or the opt-in Joint keyword). |
| `TAssign` | Assign free timber(s) to a group for addressing -- hierarchy Frame -> Bent\|Wall -> Bay\|FLOOR. |
| `TScan` | Rescan all managed timbers for coincident faces; mark the derived nodes. |
| `TPickFace` | Debug/util: interactively pick one analytic face. |
| `TUcsPlan` / `TUcsBent` / `TUcsWall` | UCS presets for placement. |
| `TRelabel` | Retrofit current label conventions onto an older frame (type-first, Dn/Up). `RelabelCommands.cs`. |

Joinery commands (each family below; every cutter has a matching `...Del`):

| Command | Connection |
|---|---|
| `TJoint` / `TJointAll` / `TJointDel` | Girt end -> post side: tenon + housing + pegs. `TJointAll` = the DELIBERATE batch: All-or-Selection scope (selection = who GETS joints), then girt->post / post->sill / summer->girt / joist->carrier passes, each with its own sticky recipe. |
| `TJointSync` | Re-cut a selected timber's joints from their STORED recipes after a move (same id) or a re-Generate (geometric re-attach to the fresh skeleton member); no-contact joints reported + left. |
| `TJoinPick` / `TJoinApply` | Joints pane flow (`TPanel` -> Joints): pick pair, edit the element stack, live re-cut. |
| `TStrut` / `TStrutDel` | Strut / v-strut -> rafter underside, post side, king-post side, girt underside (any angle). |
| `TBrace` / `TBraceDel` | Knee brace -> post/girt: same engine, 1.5" barefaced default. |
| `TRafterFoot` / `TRafterFootDel` | Principal rafter foot -> post ("girt at a pitch": sloped housing + tenon). |
| `TRafterHead` / `TRafterHeadDel` | Principal rafter head -> king-post shoulder notch. |
| `TRidge` / `TRidgeDel` | Ridge -> king post: drop-in housing (chamfered tongue). |
| `TRidgeRafter` / `TRidgeRafterDel` | Ridge -> principal rafter head: the TRidge drop-in housed into the rafter instead (king-post-less bents; once per rafter). |
| `TCommonRidge` / `TCommonRidgeDel` | Common rafter -> ridge. |
| `TCommonEave` / `TCommonEaveDel` | Common rafter -> eave girt (birdsmouth). |
| `TPurlin` / `TPurlinDel` | Purlin -> rafter (housed dovetail). |
| `TQPRafter` / `TQPRafterDel` | Queen-post rafter APEX: the male rafter's peak end seats + box-tenons into the host rafter. |

Output commands: `TBom` (sortable per-timber grid; rows highlight solids; CSV export), `TShop` /
`TShopClear` (shop maps per bent/wall + floor plans, paper-space layouts, pre-joinery arris linework,
X/+ housing marks), `TScribe` / `TScribeAll` / `TScribeProbe` (`.tsj` laser scribe export + diagnostics).

**Commands vs operations (convention):** `ManagedCommands` methods are thin command shells -- prompt,
select, resolve the sticky specs -- that delegate to static OPERATIONS (`Apply*`/`Compute*` facades,
`ManagedTimber.Build*`/`Emit*`/joint builders). New joinery lands as an operation plus a thin command
wrapper; do not put geometry math in a `[CommandMethod]` body. (The `Apply*` commit half rebuilds both
solids; the `Compute*` half mutates frames only -- shared by commands and the Joints-pane preview.)

### Managed Joinery

Fresh managed cutters on the TFrame/face model (the parked `Joints\` path is untouched). The connection
catalog + canonical param names live in `GLOSSARY.md` section D. The essentials:
- **Four LOCAL feature primitives on `TFrame`** (transform-invariant; `Faces()` ignore them, so TScan still
  reports the clean bearing node): `Features` = boxes `(Min,Max,Subtract,Joint)` (subtract=mortise,
  union=tenon); `Pegs` = cylinders `(C,Axis,R,Half,Joint)` (full/blind bores); `JointPolys` = elevation
  polygons with a width band, extruded across the section (wedge family: `TRafterFoot`); `JointPolysZ` =
  cross-section polygons extruded along the length (`TRafterHead`, `TRidge`). All id-carrying union OR
  subtract. Serialized as xrecord trailers after the base frame, in order: cuts, subtracts, Features,
  Pegs, JointPolys, JointPolysZ.
- **`StrutTenonJoint`** (`TStrut`/`TBrace`) -- ONE host-neutral end->side tenon engine, any angle: standard
  5-field spec (Thickness/Length/ShoulderTop/ShoulderBottom/Offset), world-up-keyed shoulders, tongue =
  one wall square to the host face + one along the member axis, shared-solid UNION(male)/SUBTRACT(host)
  under one joint id. `TBrace` = same engine, 1.5" barefaced default (Offset auto=(W-T)/2, Flip picks cheek).
- **`GirtPostJoint(JointSpec)`** (`TJoint`) -- cuts a shouldered tenon (independent top/bottom relish + lateral
  offset, exact-fit) + the matching mortise + peg bores. Pegs bore the POST ONLY (the shop bores the tenon
  in the field), stacked across the girt depth. `JointSpec = {TenonSpec, PegSpec}` is the session-sticky
  `_joint`, edited via the `TJoint` review loop (`ReviewJoint` / `ReviewPegs`).
- **Identity** -- each joint has a stored id shared by its tenon + mortise + pegs, so `TJoint` re-cut
  REPLACES (idempotent), `TJointDel` removes by id, and `TJointAll` batch-cuts every girt-family END that
  bears on a Post SIDE face, skipping already-cut contacts.
- **TWO GOTCHAS (durable):** (1) SOLIDHIST SAVE -- any Slice/boolean managed solid must get
  `RecordHistory=false; ShowHistory=false` AFTER `AppendEntity` (in `DrawFramedSolid` / `DrawBox`), or the
  DWG fails to save while AUDIT stays clean. (2) RELISH maps to WORLD up (`girt.Y . ZAxis`), not local +Y
  -- bent and wall girts run their depth axis opposite ways.

### Labels, floors, and output

- **Label grammar** (see the structural-label memory files + `GLOSSARY.md`): type-first
  `FAM-ANCHOR-quals` (e.g. `P-2A`, `EG-B-I`, `J-A-1`); digit-first anchor = bent/intersection,
  letter-first = wall/bay; qualifiers hand L/R, level Dn/Up, per-anchor seq. Three IDs per timber:
  location label, cut-mark (identical sticks share it -- the buy list), and the stable production
  number in the `TagHandle` slot. `TRelabel` retrofits older frames.
- **Floors (phase 1)**: `TJoist` places joist rows (Joist role, FLUSH tops, Drop); `TAssign` hierarchy is
  Frame -> Bent|Wall -> Bay|FLOOR (FloorTag; `J-<floor>-n` labels; intersection posts mint `P-2C` style
  addresses via the two-box UI). `TBrowse` is the assign + review surface (highlight, not zoom;
  double-click zooms). Phase 2 (joist dovetails + summers) and phase 3 (sills) are planned.
- **Output layer** (reads the model/XData, never re-derives geometry): `TBom` grid; `TShop` shop maps
  (per bent, per wall, floor plans; PRE-JOINERY arris linework; X/+ housing marks; 'TM Shop'
  paper-space layouts at 3/8" = 1'-0"); `TScribe`/`TScribeAll` write `.tsj` burn-path files per face
  (SOLPROF linework + ray-from-viewer annotations, cut-to-length lines both ends every face, blind-peg
  'B', 0.5" fixed text; identical braces dedupe to one drawing set + count). `TScribeProbe` explains
  per-face annotation decisions. Do NOT reintroduce surface-side visibility probes in the scribe
  annotator -- every variant regressed.

## Build

```powershell
# Build (requires Visual Studio 2022) -- from the repo root
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TimberDraw.csproj /p:Configuration=Debug /nologo /v:minimal
# Building the solution works too: MSBuild.exe TimberDraw.sln /p:Configuration=Debug
```

Output: `bin\Debug\TimberDraw.dll` -- inside the repo root (`<OutDir>bin\$(Configuration)\</OutDir>`,
NOT a `net48\` subfolder; `AppendTargetFrameworkToOutputPath=false`). Moved inside the repo 2026-07-04
(was `..\bin` in the suite container folder for APPLOAD stability across the repo split -- the APPLOAD
entry now points at the in-repo path). AutoCAD locks the DLL while loaded, so a rebuild needs AutoCAD
closed.

**Key project settings:**
- Target: `net48` (.NET Framework 4.8)
- Language: C# 9.0 (`<LangVersion>9.0</LangVersion>`)
- Assembly name: `TimberDraw`
- ObjectARX SDK: AutoCAD 2020, referenced by absolute path from `C:\Autodesk\ObjectARX_for_AutoCAD_2020_Win_64_bit\inc\`
- SDK-style .csproj -- all `.cs` files in subdirectories are included automatically
- AutoCAD command: `TDraw` (cannot use `TimberDraw` -- conflicts with assembly name)

**Source file encoding:** All `.cs` files must use **ASCII-only characters** (or UTF-8 with BOM). The .NET Framework compiler reads files in the system ANSI codepage by default. Unicode box-drawing chars, bullets, arrows, etc. in source files will cause CS1056 errors. Use plain ASCII in comments and string literals.

## Namespace Map

| Namespace | Location | Purpose |
|---|---|---|
| `TimberDraw` | All `.cs` files in root and subdirs | Core plugin code |
| `TimberFrameSuite.Standards` | `Pegs\PegSpecification.cs`, `Pegs\TFGPegStandards.cs` | Shared peg standards (future: extract to shared library) |
| `TimberScribe.Export` | `Pegs\JoineryExporter.cs` | Legacy stub (retained for reference; active export path is `Managed\Scribe*.cs` -- `TScribe`/`TScribeAll`) |

**Boundary guard:** the generator/editor split lives in the FOLDER layout, not in namespaces (all flat `TimberDraw`). `tests\TimberDraw.Architecture.Tests` text-scans the generator folders and `Managed\` and fails if one core names the other's types (only `Frame\ManagedFrameEmitter.cs` + `Frame\FrameGrid.cs` may cross, as the freeze bridge). Run it standalone (`dotnet test tests/TimberDraw.Architecture.Tests`); do NOT add it to `TimberDraw.sln`. The folder + token lists in `BoundaryTests.cs` ARE the coverage -- when you add a generator folder or a recipe/editor type, add it there.

**Domain characterization tests:** `tests\TimberDraw.Domain.Tests` (net8, xunit, standalone like the arch tests -- NOT in the .sln) compiles the PURE production sources directly (source-linked: `FrameSpec.cs`, `FrameSpecStore.cs`, `KPBentParams.cs`, `UnitInput.cs`, `Pegs\*.cs`) with a test-side stub for `Commands.ReadKPParams`. It pins the `.framespec` round-trip + every legacy migration, the bent-separation convention, distance parsing, and the TFG peg standards. `fixtures\current-kingpost.framespec` is a GOLDEN file (byte-equality) -- when the on-disk format legitimately changes, delete it, run the tests once (it regenerates + fails with instructions), and commit the new fixture in the same commit. If a linked file gains an Autodesk using, this project stops compiling -- that is the alarm. Run: `dotnet test tests/TimberDraw.Domain.Tests`.

## Architecture

### Entry Point
`Commands.cs` -- registers the frame-lifecycle commands; `TDraw` opens the tabbed shell (`Ui\Shell.cs`)
on the Frame tab. Main class is `TimberDraw.Commands`. (The legacy `UserControl1` palette was
deleted in Phase C, 2026-07-06.)

### Global State: `Module1.cs`
Slimmed to the timber XDATA layer (deep purge 2026-07-06): `DataStructure` + `GetXdata`/`SetXdata`
(the extension-dictionary schema -- field names and TypedValue codes are the ON-DISK FORMAT, do not
touch), plus `BuyLongFeet` and `EraseEntity`. Everything else (draw helpers, cross-bent queues,
cascade machinery, connection xrecords) went with the legacy pipeline.

### Peg standards (`Pegs\`)
`TFGPegStandards`/`PegSpecification` (namespace TimberFrameSuite.Standards): TFG peg-sizing
reference data, currently unreferenced by the managed path; kept for the future shared library.
`JoineryExporter.cs` is a legacy stub retained for reference.

## Timber Frame Suite

| App | Status | Role |
|---|---|---|
| **TimberDraw** (this project) | Active | Managed timber substrate: draws solids, cuts joinery, labels/BOM/shop maps, scribe export (`TScribe`/`TScribeAll`) |
| **TimberScribe** | Active | Raspberry Pi Flask server -- receives `.tsj` files, drives laser print head |
| **TimberTag** | **LEGACY -- superseded** | Old AutoCAD plugin -- tags, BOM, scribe face extraction, TRegenTimber UI. All roles absorbed into TimberDraw's managed path; only needed for drawings made by the legacy parametric pipeline. Its `.dxf` reference writer was never ported. |

**XData note:** `TagHandle` on each timber entity is written as `"0"` at draw time
(placeholder). Legacy TimberTag wrote its MText tag entity's handle into this slot
via the `TT` command. On MANAGED timbers the slot is repurposed as the stable
production number, preserved across rebuilds by `RebuildFromFrame` in
`Managed\ManagedTimber.cs`. The `"0"` at draw time is correct and intentional --
do not change it.

**Size xdata:** All structural timber `DrawElement` calls already pass a
`sizeStr` of the form `"WxDxL"` (integer inches, BuyLongFeet for length).
Joinery types (Tenon, Mortise, Housing, DoveTail) intentionally pass `""`.

`JoineryExporter.cs` is a legacy stub retained for reference. TimberScribe is a
standalone Pi application that consumes `.tsj` files from TimberDraw's managed
scribe export (`Managed\ScribeTsj.cs` keeps the schema byte-compatible with the
original TimberTag writer); there is no shared `TimberFrameSuite.Standards.dll`
dependency in the current architecture.

## Legacy Parametric Pipeline -- REMOVED (Phase C deep purge, 2026-07-06)

The entire legacy parametric pipeline is deleted (Robert's call): the member classes (`Bent\`,
`Bay\`, `Kpost\`, `Qpost\`, `Hbeam\`, `KpostTruss\`, `QpostTruss\`), the cross-bent mortise
queues, `TimberFactory` + the delta-swap cascade, the `Joints\` generator registry, `Network\`
(BentNetwork/NetworkManager), `FrameRenderer`, and the legacy palettes/commands (`TDrawLegacy`,
`TFrameFlat`, `TRegenTimber`, `TSave`/`TLoad`, `TFrame*`, `TFrameSave`/`TFrameLoad`).

Drawings made by that pipeline still OPEN fine -- their xdata schema lives on in
`Module1.DataStructure` -- but per-timber parametric regen for them is no longer supported; the
managed path is the only pipeline. The old design docs live in git history (last complete at
commit 1ad54d9).

## Common Pitfalls

- **Non-ASCII in source**: Any Unicode character in a `.cs` file (comments, strings, anywhere) will break the ANSI-mode compiler. Use `x` not `x`, `-` not bullet, `->` not arrow, etc.
- **Assembly name vs command**: The assembly is `TimberDraw.dll` and namespace is `TimberDraw`. The AutoCAD command must be `TDraw` -- using `TimberDraw` as the command name conflicts with the assembly and will not register.
- **`new()` syntax**: Requires C# 9.0+. The project is set to 9.0. Do not revert to 8.0 without replacing all `new()` calls with explicit types.
- **ObjectARX hint paths**: References use absolute paths (`C:\Autodesk\ObjectARX_for_AutoCAD_2020_Win_64_bit\inc\`). Relative paths in the old `.csproj` did not resolve correctly from this directory depth.
