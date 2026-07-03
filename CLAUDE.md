# TimberDraw -- Claude Code Instructions

## What This Project Is

TimberDraw is a **.NET 4.8 AutoCAD plugin** (class library) that draws timber frame bent geometry in AutoCAD. It is the first application in the **Timber Frame Suite**.

The plugin loads into AutoCAD via `NETLOAD` and exposes a single command (`TDraw`) that opens a palette with a Windows Forms UI. Users configure bent parameters, then click to draw 2D or 3D timber members directly in the current drawing.

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

Full rationale and the labeling convention: see `timberdraw-architecture-decisions.md`.

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

`Managed\ManagedTimber.cs` -- the managed timber model + the editor:
- `TFrame` struct -- a timber's WCS frame (`O`, axes `X/Y/Z`, `L/D/W`) + end-cut normals + feature lists.
  `Faces(TFrame)` returns the 6 nominal faces; `ComputeNodes` derives connectivity from face coincidence
  (`FacesMate`), surfaced by `TScan`. `BuildFramedSolid` builds the solid (box -> slice end cuts -> convex
  Cuts -> concave Subtracts -> joinery Features/Pegs); `DrawFramedSolid` / `RebuildFromFrame` persist
  everything as XData trailers and preserve the production `TagHandle`.

### Editor commands (all in `Managed\ManagedTimber.cs`)

| Command | Description |
|---|---|
| `TPlace` | Place one managed timber; pick the extrusion direction (no UCS re-roll per member). |
| `TSpan` | Connect two PICKED timbers -- finds the facing faces and fills the gap with a member. |
| `TJoin` | Connect two PICKED faces -- facing -> square-ended filler; angled -> mitered knee brace. |
| `TFit` | Trim/extend a timber's picked END onto a target face (square or mitered); other end stays. |
| `TSection` | Re-section a managed timber (change W x D) in place. |
| `TScarf` | Scarf-splice a timber into two pieces; stores the scarf interface node. |
| `TJoist` | Place a row of floor joists in a wall/bay (free-assembly infill). |
| `TAssign` | Assign free timber(s) to a bent/wall group for grid addressing. |
| `TScan` | Rescan all managed timbers for coincident faces; mark the derived nodes. |
| `TPickFace` | Debug/util: interactively pick one analytic face. |
| `TUcsPlan` / `TUcsBent` / `TUcsWall` | UCS presets for placement. |
| `TJoint` / `TJointDel` / `TJointAll` | Girt->post joinery -- cut one / delete one / batch-cut all (below). |

### Managed Joinery (girt -> post mortise & tenon + pegs)

Fresh managed cutters on the TFrame/face model (the parked `Joints\` path is untouched). Full depth in the
`managed-joinery-v1` memory; the essentials:
- **Two LOCAL feature primitives on `TFrame`** (transform-invariant; `Faces()` ignore both, so TScan still
  reports the clean bearing node): `Features` = boxes `(Min,Max,Subtract,Joint)` (subtract=mortise,
  union=tenon); `Pegs` = cylinders `(C,Axis,R,Half,Joint)` (full/blind bores). Serialized as xrecord
  trailers after the base frame, in order: cuts, subtracts, Features, Pegs.
- **`GirtPostJoint(JointSpec)`** -- cuts a shouldered tenon (independent top/bottom relish + lateral
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

## Build

```powershell
# Build (requires Visual Studio 2022) -- from the repo root
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TimberDraw.csproj /p:Configuration=Debug /nologo /v:minimal
# Building the solution works too: MSBuild.exe TimberDraw.sln /p:Configuration=Debug
```

Output: `..\bin\Debug\TimberDraw.dll` -- ONE LEVEL ABOVE the repo root (`<OutDir>..\bin\$(Configuration)\</OutDir>`,
NOT a `net48\` subfolder; `AppendTargetFrameworkToOutputPath=false`). Deliberately kept after the repo split
so the user's AutoCAD APPLOAD/NETLOAD startup path (`...\Timber Frame Suite\bin\Debug\TimberDraw.dll`) stays
stable; TODO before going public: move OutDir inside the repo and update the APPLOAD entry. AutoCAD locks the
DLL while loaded, so a copy after build may need a retry.

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

## Architecture

### Entry Point
`Commands.cs` -- registers the `TDraw` AutoCAD command, opens the palette containing `UserControl1`. Main class is `TimberDraw.Commands`.

### Global State: `Module1.cs`
A `static` class that holds all current bent parameters as shared state:
- `Module1.Span`, `Module1.EaveHt`, `Module1.Pitch`, `Module1.Beta` -- geometry
- `Module1.Make3D` -- 2D vs 3D mode
- `Module1.HasJoinery` -- DEPRECATED: always true in parametric model; guards in member classes still present but will be removed in Phase 2 member migration
- `Module1.HasShoulder` -- whether beams have shouldered seats
- `Module1.OffsetType` -- Back / Centered / Front (strut/brace placement in 3D)
- `Module1.DrawElement(pts, width, type, bentNumber, designation)` -- the main draw function; extrudes a polyline profile to create a solid timber
- `Module1.DrawPeg(pt, radius, length, ...)` -- draws a cylindrical peg
- `Module1.AddJoint(timber, joinery, jointType)` -- attaches joinery xdata to a timber entity
- `Module1.AtPoint(pt, dx, dy, dz)` -- offset helper
- `Module1.PolarPoint(pt, angle, distance)` -- polar offset helper

**Cross-bent pending-mortise queues** (generation-time scaffolding -- live across TDraw calls during the parametric phase, discarded at the freeze; see Architecture Direction):

```csharp
// Posts
public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingLeftPostMortises   = new();
public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingRightPostMortises  = new();
// Rafters
public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingLeftRafterMortises  = new();
public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingRightRafterMortises = new();
// King post / hammer beam king post
public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingKPostMortises = new();
```

Each TDraw call **consumes** the queues immediately after `<BENT>.Draw()`, then **clears** them. Bay-member drawing then **populates** them with far-end IDs for the following call. See "Cross-Bent Mortise Wiring" section below.

### Bent Types and Subdirectory Layout

```
TimberDraw\
  Bent\                         -- Bent-plane geometry generators
    PostLeft.cs / PostRight.cs  -- Wall posts (common to all bent types)
    BentGirt.cs                 -- Horizontal tie at eave height
    BentBrace.cs                -- Diagonal knee brace (TenonWidth = 1.5")
    FloorBentGirt.cs            -- Floor-level girt in bent plane
    TrussGirt.cs                -- Girt for truss bents

  Bay\                          -- Bay geometry generators (span between bents)
    BayBrace.cs                 -- Bay brace (TenonWidth = 1.5")
    EaveGirt.cs                 -- Girt at eave line (complex geometry, separate L/R tenon blocks)
                                   Fields: TenonLeftId, TenonRightId (near-end)
                                           TenonLeftFarId, TenonRightFarId (far-end, queued for next bent)
    FloorBayGirt.cs             -- Floor-level girt across bays
                                   Fields: TenonLeft1Id/TenonRight1Id (near-end)
                                           TenonLeft2Id/TenonRight2Id (far-end, queued for next bent)
    Ridge.cs                    -- Ridge beam
                                   Fields: Housing1Id/Housing1aId (near-end, two copies for QPBent)
                                           Housing2Id/Housing2aId (far-end, queued for next bent kpost/rafters)
                                           RidgeExtension1_ID, RidgeExtension2Id (tenon stubs, currently deleted)
    Purlins.cs                  -- Roof purlins; requires "using System.Collections.Generic"
                                   Fields: ExtensionLeftCol/ExtensionRightCol (near-end, ObjectIdCollection)
                                           ExtensionLeftFarList/ExtensionRightFarList
                                             (far-end, List<(ObjectId ExtId, ObjectId PurlinId)>,
                                              queued for next bent's rafters)
    Commons.cs                  -- Common rafters (bay-interior only, no cross-bent wiring needed)

  Kpost\      -- King Post bent (KPBent.cs orchestrates: KPost, KPRafterLeft/Right, KPStrutLeft/Right, KPVertStrutLeft/Right)
  Qpost\      -- Queen Post bent (QPBent.cs)
  Hbeam\      -- Hammer Beam bent (HBBent.cs, HBeamLeft/Right, HPostLeft/Right, HBGirt, HBKpost, HBBayGirt)

  KpostTruss\ -- King Post Truss variant (KPTruss.cs)
  QpostTruss\ -- Queen Post Truss variant (QPTruss.cs) -- no DrawPeg calls

  Pegs\       -- TFG peg standards (TimberFrameSuite.Standards namespace)
    PegSpecification.cs
    TFGPegStandards.cs
    JoineryExporter.cs    -- Legacy stub; active scribe export is Managed\Scribe*.cs (TScribe/TScribeAll)
    INTEGRATION_GUIDE.md  -- Reference guide (original design; TimberScribe is now a Pi Flask server)

  Joints\     -- Phase 1: JointFactory registry and all 12 joint type generators
    IJointGenerator.cs       -- interface: Generate(JointParams) + RequiredExtras[]
    JointParams.cs           -- struct: Origin, FaceNormal, LateralDir, Width, Depth, TenonWidth,
                                        Pitch (timber slope, 0=horizontal), Extra dict
    JointFactory.cs          -- static: Register(), Create(), RequiredExtras(), RegisterDefaults()
    TenonGenerator.cs        -- working: tenon with sloped top face when JointParams.Pitch > 0
                                  RequiredExtras: ["hasShoulder","tenonWidth","tenonRelish","housingDepth","Pitch"]
    MortiseGenerator.cs      -- working: matching envelope to TenonGenerator (slope-aware)
    ShoulderGenerator.cs     -- working: right-triangle shoulder at rafter-to-kingpost peak
                                  RequiredExtras: ["sitdepth"]
                                  Geometry: s0=bearing corner, s1=s0+LateralDir*(sitdepth/sin(Beta)),
                                            s2=s0+FaceNormal*sitdepth (right angle at s2)
                                  FaceNormal = rafter axis (cos Beta, sin Beta, 0) / (-cos Beta, sin Beta, 0)
    ButtGenerator.cs         -- stub: returns ObjectId.Null (bearing face, no geometry)
    ButtHousingGenerator.cs  -- stub
    DovetailGenerator.cs     -- stub (RequiredExtras: TaperAngle, TenonLength)
    DovetailHousingGenerator.cs -- stub
    BirdmouthGenerator.cs    -- stub (RequiredExtras: Pitch, SeatDepth)
    BirdmouthHousingGenerator.cs -- stub
    ScarfAGenerator.cs       -- stub (RequiredExtras: ScarfLength)
    ScarfBGenerator.cs       -- stub
    SplineGenerator.cs       -- stub
    SplineHousingGenerator.cs -- stub

  TimberFactory.cs -- Phase 2: parametric regeneration dispatcher
    Regenerate(Handle, newWidth, newDepth, newJointNear, newJointFar)
    RegenerateBentGirt() -- WORKING: erases old timber+tenons+pegs, marks neighbours stale, redraws
    Other types: NotImplementedException (add RegenerateXxx() + switch case to extend)
```

Each member class follows the same pattern:
```csharp
public class KPost
{
    // Public fields set by the orchestrating Bent class
    public Point3d StartPoint;
    public double Width, Depth;
    private double TenonWidth = 2;   // or 1.5 for braces
    public string BentNumber, Designation, Type;
    public ObjectId Timber;          // entity handle of drawn timber
    public List<ObjectId> PegCol;    // peg entity handles

    public void Draw() { ... }       // draws timber + tenon + pegs
    public void AddMortise(ObjectId MortiseId) { ... }  // attaches mortise xdata
}
```

Note: most member classes use `TimberId` for the entity handle. `HBeamRight` is the
exception and uses `TimberID` (capital ID). `HBGirt`, `HBBayGirt` still use `Timber`.
KPost and HBKpost were migrated to `TimberId` in the 2026-06-02 session.

### Cross-Bent Mortise Wiring

> **Reframed 2026-06-16 -- see Architecture Direction.** These five queues are **generation-time scaffolding** used while the generator emits a frame; they are discarded at the freeze and are NOT a persistent connectivity engine. Do not extend them to walls, floors, sub-walls, or partial sub-bents -- those are free-assembly infill, connected by face coincidence (TScan).

Each TDraw call draws ONE bent plus optional bay members. Bay members (EaveGirt, FloorBayGirt, BayBrace, Ridge, Purlins) span from the current bent to the NEXT bent. Their tenons at BOTH ends are drawn as solids, but the far-end tenons cannot be wired to the next bent's members at draw time because those members do not exist yet.

**Solution:** Five static `List<(ObjectId MortiseId, ObjectId SourceTimberId)>` queues in Module1 that survive between TDraw calls.

**Execution order per TDraw call:**

```
1. Draw bent (HB / KPB / QPB / KPT / QPT)
2. Consume all 5 pending queues into the new bent's members:
     - PendingLeftPostMortises  -> bent.PLeft.AddMortise + AddConnection
     - PendingRightPostMortises -> bent.PRight.AddMortise + AddConnection
     - PendingLeftRafterMortises  -> bent.RLeft.AddMortise + AddConnection
     - PendingRightRafterMortises -> bent.RRight.AddMortise + AddConnection
     - PendingKPostMortises -> bent.KP.AddMortise + AddConnection
   (truss types: Clear-only -- no exposed wall posts, rafters, or kpost)
3. Clear all 5 queues (unconditional)
4. [optional] Draw bay members and populate queues:
     EaveGirt    -> TenonLeftFarId  -> PendingLeftPostMortises
                    TenonRightFarId -> PendingRightPostMortises
     BayBrace (far-bay left)  TenonDown -> PendingLeftPostMortises
     BayBrace (far-bay right) TenonUp   -> PendingRightPostMortises
     FloorBayGirt -> TenonLeft2Id  -> PendingLeftPostMortises
                     TenonRight2Id -> PendingRightPostMortises
     FlrGirtBrace (far-bay left)  TenonDown -> PendingLeftPostMortises
     FlrGirtBrace (far-bay right) TenonUp   -> PendingRightPostMortises
     Ridge (HB/KPB) -> Housing2Id  -> PendingKPostMortises (Housing2aId deleted)
     Ridge (QPB)    -> Housing2Id  -> PendingLeftRafterMortises
                       Housing2aId -> PendingRightRafterMortises
     Ridge (truss)  -> Housing2Id and Housing2aId both deleted immediately
     Purlins -> ExtensionLeftFarList  -> PendingLeftRafterMortises
                ExtensionRightFarList -> PendingRightRafterMortises
```

On the first TDraw call the queues are empty so step 2 is a no-op. Safe.

**Member variable names for consumption (UserControl1.cs):**

| Bent | Left rafter | Right rafter | KPost / Timber field |
|---|---|---|---|
| HBBent `HB` | `HB.HBRLeft` / `.TimberId` | `HB.HBRRight` / `.TimberId` | `HB.HBKP` / `.TimberId` |
| KPBent `KPB` | `KPB.RLeft` / `.TimberId` | `KPB.RRight` / `.TimberId` | `KPB.KP` / `.TimberId` |
| QPBent `QPB` | `QPB.QPRLeft` / `.TimberId` | `QPB.QPRRight` / `.TimberId` | (none) |

### Peg Standards (`Pegs\`)

Peg sizing is always from `TFGPegStandards.GetPresetForTenonThickness(TenonWidth)`:

| TenonWidth | Peg diameter | First peg setback | Spacing |
|---|---|---|---|
| 2.0" (most members) | 1.0" (r = 0.5) | 2.0" | 3.5" |
| 1.5" (braces) | 0.75" (r = 0.375) | 2.0" | 2.625" |

First peg is always placed; subsequent pegs only if `y <= Depth - FirstPegSetback`.

**Migrated members** (BentGirt, FloorBentGirt): pegs are generated inside `TenonGenerator`
when `JointParams.GeneratePegs = true`. No `PegIt()` method, no `using TimberFrameSuite.Standards`
needed in the member class. Pegs sit 1.75" from the tenon near face (= housing back when
HousingDepth > 0).

**Unmigrated members** still use inline peg blocks or `PegIt()` keyed to `TenonWidth`.
Notable cases:
- EaveGirt, HBBayGirt: two separate `if (HasJoinery)` blocks; declare peg vars
  (`r`, `maxPegPos`) independently in each block.
- Strut/post members: single peg per joint; polar offset is joint-specific.

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

## Parametric Timber Model (Phase 1 + Phase 2 + Phase 3)

### Strategy

> **Reframed 2026-06-16 -- see Architecture Direction.** This describes the **generator's** behavior during the parametric (pre-break) phase, not a persistent live model. "Every timber is a parametric first-class object" holds while the generator owns it; after the break a timber is a plain managed timber edited via the editor, and the cascade/stale machinery here no longer applies to it.

Every timber is a parametric first-class object. Changing Width/Depth or JointType
triggers a full redraw via `TimberFactory.Regenerate()`. Connected timbers that the
cascade could not regenerate are marked stale (IsStale xdata flag); TimberTag shows
the stale indicator and the user clicks Regenerate per timber. **HasJoinery is always
true** -- joinery is always generated.

### Extended XData Schema (Phase 1)

In addition to the original fields (Type, BentNumber, Designation, Size, TagHandle,
TenonCnt, MortiseCnt, PegCnt), every timber now carries:

| Key | Type | Content |
|---|---|---|
| `Width` | double | Section width in inches (= extrusion depth in DrawElement) |
| `Depth` | double | Section depth in inches (parsed from Size "WxDxL" string) |
| `Length` | double | Nominal length in inches (buy-long feet x 12) |
| `JointNear` | string | JointType name at near/bottom/left end (e.g. "Tenon"). **Every timber must have a non-empty value.** |
| `JointFar`  | string | JointType name at far/top/right end. **Every timber must have a non-empty value.** |
| `IsStale` | bool | true when a connected timber has changed since last regen |
| `TenonHandles` | string | Comma-separated hex handles of standalone tenon solids |
| `PegHandles` | string | Comma-separated hex handles of peg cylinder solids |
| `DrawContext` | string | JSON snapshot of Module1 globals + member params at draw time |
| `JointNearParams` | string | User-editable JSON scalars for near joint (tenonWidth, tenonRelish, housingDepth, etc.). Written by TimberDraw on draw AND by TimberTag on every property-grid change. |
| `JointFarParams`  | string | Same for far joint. |
| `JointNearParamsDrawn` | string | **Snapshot of the params physically drawn last time.** Written ONLY by `Module1.SetXdata()` (TimberDraw), never by `Timber.SetXdata()` (TimberTag). Source of "old params" in `ApplyCascade` delta-swap even when TimberTag has already overwritten `JointNearParams`. |
| `JointFarParamsDrawn`  | string | Same for far joint. |

`TenonHandles` is auto-populated by `Module1.AddJoint(Joint.Tenon)`.
`PegHandles` and `DrawContext` must be written explicitly by each member's `Draw()`.

**Invariant:** `JointNear` and `JointFar` must be non-empty on every structural timber.
They are set by passing `jointNear:` and `jointFar:` named arguments to `DrawElement`/`DrawBrace`
at the end of each member's `DrawElement`/`DrawBrace` call.
Defaults are **hardcoded per member type** in each `Draw()` method -- the joint type is
determined by the member's structural role, not by a palette parameter.
The regen dialog (TimberTag) is the post-draw override path for per-timber joint type changes.

**BentGirt.cs** and **FloorBentGirt.cs** are the reference implementations.

### Unified Factory Pattern

All migrated member factories follow this shape in `Draw()`:

```
1. Body: plain face-to-face rectangle -- no HasShoulder branch.
   DrawElement(..., jointNear:"Tenon", jointFar:"Tenon") -> TimberId

2. Per-end joint params from NearParams/FarParams dicts (fall back to member defaults):
   double nearTenonWidth   = NearParams["tenonWidth"]   ?? 2.0
   double nearTopRelish    = NearParams["tenonRelish"]  ?? 0.0
   double nearShoulder     = NearParams["hasShoulder"]  ?? (HasShoulder ? 1.0 : 0.0)
   double nearHousingDepth = NearParams["housingDepth"] ?? 0.0
   // repeat for far end

3. One call per end -- TenonGenerator owns all joint geometry and pegs:
   JointResult r = JointFactory.CreateWithPegs(JointType.Tenon, new JointParams(
       ..., topRelish, shoulderDepth, generatePegs: true, housingDepth));
   TenonLeftId = r.JointId;      // single unified solid (housing+tenon+shoulder)
   PegCol.AddRange(r.Pegs);

4. Module1.AddJoint(TimberId, TenonLeftId, Joint.Tenon)  // unite into body

5. Persist:
   Module1.PersistPegHandles(TimberId, PegCol)
   Module1.SaveDrawContext(TimberId, BuildContextJson())
   // Write joint params to XData so TimberTag reads correct initial values:
   xd.JointNearParams = "{\"hasShoulder\":...,\"tenonWidth\":...,
                          \"tenonRelish\":...,\"housingDepth\":...}"
   Module1.SetXdata(TimberId, xd)
```

**JointResult** (`Joints/JointResult.cs`): `ObjectId JointId` + `List<ObjectId> AdditionalSolids`
(unused for Tenon; reserved for future joint types) + `List<ObjectId> Pegs`.

**TenonGenerator** (`Joints/TenonGenerator.cs`) -- all geometry in one solid:
- **Winding fix**: `LateralDir x FaceNormal` Z-sign check reverses vertices for
  right-end joints (+X FaceNormal) so every profile extrudes in +Z.
- **Slope support** (`JointParams.Pitch > 0`): the top face of both tenon and housing
  tilts at the timber's own pitch so the tenon top aligns with the timber top edge.
  `topFaceSlope = -Pitch`. The `lateralSpan` formula accounts for housing depth:
  `lateralSpan = Depth - TopRelish + HousingDepth * topFaceSlope`.
  Backward-compat: Pitch = 0 (default) gives flat tops unchanged.
- **Housing** (HousingDepth > 0): 4-pt parallelogram (sloped top when Pitch > 0), full
  Width x full Depth, HousingDepth deep. Tenon origin shifts to the housing back.
  BoolUnited into tenon, original deleted.
- **Tenon**: parallelogram (sloped top when Pitch > 0), TenonWidth wide, 4" deep.
- **Shoulder** (ShoulderDepth > 0): 3-pt triangle (face-bot, face-top, seat-bot),
  full Width wide. BoolUnited into tenon, original deleted.
- **Pegs** (GeneratePegs): 1.75" from tenon near face, spaced per TFGPegStandards.
- **RequiredExtras**: `["hasShoulder", "tenonWidth", "tenonRelish", "housingDepth", "Pitch"]`
  -- drives TimberTag's joint-params UI.

**MortiseGenerator** (`Joints/MortiseGenerator.cs`): mirrors TenonGenerator slope so
the post void matches the tenon exactly. Also has winding fix for right-side mortises.

**JointParams key fields** (optional constructor params, all default 0/false):
`TopRelish`, `ShoulderDepth`, `GeneratePegs`, `HousingDepth`, `Pitch`.

**Rafter foot tenon (KPRafterLeft/Right, QPRafterLeft)**: passes `pitch: Module1.Pitch`
and `Depth = Module1.PlumbLength` so the sloped top face aligns with the rafter body.
The old `Depth = PlumbLength - (1.0 * Pitch)` workaround is removed.

**Migrated members:** `BentGirt`, `FloorBentGirt`, `KPost`, `HBKpost`, `KPRafterLeft/Right`.

**Migration checklist per member:**
1. Body: remove `if (HasShoulder)` branch -- always plain rectangle.
2. Add `NearParams`/`FarParams` fields (`Dictionary<string,double>`) if not present.
3. Read per-end params (tenonWidth, tenonRelish, hasShoulder, housingDepth).
4. Replace inline tenon+peg block with `CreateWithPegs(...)` calls. Remove `PegIt()`.
5. For SLOPED members (rafters): pass `pitch: Module1.Pitch` and `Depth = Module1.PlumbLength`.
   For HORIZONTAL/VERTICAL members: omit `pitch` (defaults to 0).
6. Write `JointNearParams`/`JointFarParams` XData after draw (Module1.SetXdata also auto-writes
   `JointNearParamsDrawn`/`JointFarParamsDrawn` -- the delta-swap source of truth).
7. Delete old `PegIt()` and `using TimberFrameSuite.Standards`.
8. In `TimberFactory.RegenerateXxx()`: populate `NearParams`/`FarParams` from
   `DeserializeJointParams(data.JointNearParams/FarParams)`.
9. Add a `case "ClassName":` in `GetTenonParamsGeometric` -- reads scalars via `ReadScalars()`
   which respects the `nearOverride`/`farOverride` params used by the delta-swap cascade.
10. In the orchestrator: use `AddConnectionFull` (not `AddConnection`) for every tenon-giving
    connection so `ApplyCascade` can take the rich delta path.

**DrawContext startX/Y/Z convention (NEW -- applies to migrated members):**
- `startX/Y/Z` stores the member's own `StartPoint.X/Y/Z` (local origin within the bent).
- `RegenerateXxx` sets `Module1.StartPoint = new Point3d(0, 0, ctx.StartZ)` (bent global),
  then sets `member.StartPoint = new Point3d(ctx.StartX, ctx.TOG, ctx.StartZ)` from the
  stored local origin.
- Old convention (BentGirt etc.): `startX/Y/Z` stored `Module1.StartPoint` (bent global);
  local origin recomputed from formula. Do NOT mix conventions within one member.

**King Post body** (`Kpost/KPost.cs`): pentagonal cross-section whose top-left and
top-right faces slope at `Pitch` to meet the rafter bottom surfaces exactly at each
kingpost side face. `pts[4]=(halfSpan-Depth/2, EaveHt+(halfSpan-Depth/2)*Pitch)` and
`pts[2]=(halfSpan+Depth/2, same y)` are the top corners; `pts[3]=(halfSpan, peak)` is
the ridge point. The two sloped top faces are co-planar with each rafter's bottom face.

**Rafter-to-kingpost shoulder** (`ShoulderGenerator.cs`, called from KPRafterLeft/Right):
Right-triangle notch with right angle at `s2` (end of leg `a`).
- `a = sitdepth` (default 3"), along the rafter axis (FaceNormal) -- user-adjustable via
  `NearParams["sitdepth"]`, stored in `JointNearParams` JSON, surfaced to TimberTag via
  `ShoulderGenerator.RequiredExtras = ["sitdepth"]`.
- `b = sitdepth * cot(Beta)` -- perpendicular to rafter axis, back to plumb face (varies).
- `c = sitdepth / sin(Beta)` -- closing side on the plumb face (varies).
- Equal legs (a==b) only at 45-deg pitch. FaceNormal = `(cos B, sin B, 0)` left /
  `(-cos B, sin B, 0)` right. Caller passes `Depth = sitdepth / sin(Beta)`.
- `SeatPeakId` is united into the rafter (`Joint.Tenon`) and subtracted from the
  kingpost (`KPost.AddMortise` in KPBent line 313).

### Delta-swap cascade (Phase 4)

When a giver (e.g. BentGirt) is regenerated with changed joint params, the rich cascade
path does NOT erase/redraw the receiver (PostLeft). Instead it does a targeted in-place
joint replacement that preserves all other mortises on the receiver.

**Key data flow:**

1. `Module1.SetXdata()` (TimberDraw only) always writes:
   - `JointNearParamsDrawn = JointNearParams` (snapshot of as-drawn state)
   - TimberTag's `Timber.SetXdata()` never writes these fields.

2. `Regenerate()` captures before/after JSON **before** dispatch:
   ```csharp
   string oldNearJson = data.JointNearParamsDrawn ?? data.JointNearParams;
   ```
   TimberTag has already overwritten `JointNearParams` with the new relish by the time
   Regen runs, so `JointNearParams` = new. `JointNearParamsDrawn` = old (as drawn). ✓

3. `ApplyCascade` builds exact before/after `JointParams[]`:
   ```csharp
   JointParams[] oldParamsArr = GetTenonParamsGeometric(newPrimaryId, oldNearJson, oldFarJson);
   JointParams[] newParamsArr = GetTenonParamsGeometric(newPrimaryId);
   ```
   Same DrawContext geometry, different scalar JSON = exact old/new tenon solids.

4. `DeltaSwapJoint(receiverId, oldParams, newTenonId)`:
   - `JointFactory.Create(oldParams)` → fill solid → `AddJoint(Fill)` → BoolUnite (fills old void)
   - `AddJoint(newTenonId, Mortise)` → BoolSubtract (cuts new void)

5. `Joint.Fill = 2` in `Module1.Joint` enum -- BoolUnite, no count updates, no IncomingMortises write.

6. Delta-updated receivers are added to `HashSet<Handle> deltaUpdated` returned by `ApplyCascade`.
   The stale-marking loop skips them (`if (deltaUpdated.Contains(h)) continue`).

**`GetTenonParamsGeometric` override params:**
All cases call `ReadScalars(xd, ..., nearJson, farJson)` where `nearJson`/`farJson`
replace the stored XData scalars. Pass `null` for both to use current XData (normal path).

**`IncomingJoints` xrecord** (`SaveIncomingJoint`, `LoadIncomingJoint` in Module1):
Exists but is NOT used in the current cascade flow. Reserved for future use (e.g. direct
regen of a receiver that needs to re-cut all incoming joints by params).

### IncomingMortises xrecord (Phase 3)

Every receiver timber (post, king post, etc.) stores its received mortise bounding
boxes in an extension-dictionary xrecord named `IncomingMortises`.

- **Written by** `Module1.SaveIncomingMortise(timberId, Extents3d)` -- called inside
  `Module1.AddJoint()` at the end of every successful `Joint.Mortise` operation (3D mode only).
  Appends a `(code 10, minPt) + (code 11, maxPt)` pair.
- **Read by** `Module1.LoadIncomingMortises(timberId)` -- returns `Extents3d[]`;
  empty array if xrecord absent.
- **Used during direct update** (user regenerates a receiver timber directly):
  `TimberFactory.EraseAndMarkStale()` loads the bounding boxes from the *old* entity
  before erasing it, stores them in `_pendingIncomingMortises`, and
  `TimberFactory.ApplyJointTypes()` re-cuts them into the new entity via
  `ApplyIncomingMortises()`.  Each re-cut calls `AddJoint(Mortise)` which writes the
  boxes back to `IncomingMortises` on the new entity, keeping the xrecord current.
- **Skipped during cascade** (`_isCascading=true`): the tenon-giver's `AddJoint(Mortise)`
  cuts fresh mortises and rebuilds the xrecord, so the save-before-erase step is
  skipped -- `_pendingIncomingMortises` is set to empty for those calls.

### Connected-member handle staleness fix (Phase 3)

When a connected timber is cascade-regenerated, its AutoCAD entity is erased and a
new one is created with a different `ObjectId`/`Handle`.  Without correction, the
primary timber's `Connections` xrecord still holds the old (erased) handle.  On the
NEXT regen of the primary, `ApplyCascade` sees `cid.IsErased` and skips the
connection entirely, leaving the connected member's old mortise shape and a
disconnected tenon solid floating in the drawing.

**Fix:** `Module1.UpdateConnectionHandle(timberId, oldHandle, newHandle)` is called in
both `ApplyCascade` and `ApplyCascadeGeometric` immediately after each successful
inner `Regenerate()`.  It rewrites all three connection xrecords (`Connections`,
`ConnectedMembers`, `ConnectionEndpoints`) on the primary timber, replacing every
occurrence of `oldHandle` with the new live `Handle`.

### Selection-set update after regen (Phase 3)

When `TRegenTimber` runs, TimberTag's selection set (SS) may contain ObjectIds for the
primary timber AND for cascade-regenerated connected members.  All regenerated entities
get new ObjectIds; the old ones are erased.  Without correction, SS navigation fails
on any erased slot.

**Fix:**  
- `TimberFactory._regenMap` (`List<(Handle Old, Handle New)>`) accumulates every
  (oldHandle→newHandle) pair for the entire operation: primary first, then each
  cascade result.
- After cascade, `Commands.WriteRegenMap(db, _regenMap)` writes the map to
  `NOM["TRegenMap"]` as one Text TypedValue per pair (`"oldHex:newHex"`).
- In TimberTag, `OnRegenCommandEnded` reads and clears the map, then calls
  `RebuildSSAfterRegen(Dictionary<Handle,Handle>)` which walks the entire SS array,
  resolves new ObjectIds for every handle present in the map, and rebuilds the SS
  in place.  Count stays constant; `CurrTimberIndex` is clamped to the new count.

### ConnectedMembers format update (Phase 1)

Two parallel xrecords now exist on each timber:
- `ConnectedMembers` -- unchanged triplet (Handle + JointType Int16 + Color 256 sentinel)
  Read by old TimberTag; backward-compat.
- `ConnectionEndpoints` -- new quad (Handle + ThisEnd Int16 + OtherEnd Int16 + Color 0 sentinel)
  Read by new TimberTag (Phase 3). ThisEnd/OtherEnd = 0 (Near) or 1 (Far).

`Module1.AddConnection()` signature: `(timberId, connectedId, connection, thisEnd=0, otherEnd=0)`.

### Module1 public API additions (Phase 2)

```csharp
// Xdata access
Module1.GetXdata(ObjectId)           // now public
Module1.SetXdata(ObjectId, DataStructure)
Module1.GetConnectedHandles(ObjectId) // returns Handle[] from ConnectedMembers
Module1.GetConnections(ObjectId)      // returns Connection[] from Connections xrecord

// Handle tracking
Module1.AppendHandleToField(timberId, key, entityId) // appends to CSV text xrecord
Module1.GetHandlesFromField(timberId, key)            // returns Handle[]
Module1.PersistPegHandles(timberId, List<ObjectId>)   // writes "PegHandles"
Module1.SaveDrawContext(timberId, json)               // writes "DrawContext"
Module1.LoadDrawContext(timberId)                      // reads "DrawContext", "{}" if absent

// Stale + erase
Module1.MarkStale(timberId)           // sets IsStale = true
Module1.GetObjectIdFromHandle(Handle) // db.TryGetObjectId wrapper
Module1.EraseEntities(Handle[])       // batch erase by handle
Module1.EraseEntity(ObjectId)         // single entity erase
```

### Module1 public API additions (Phase 3)

```csharp
// Receiver-driven mortise persistence
Module1.SaveIncomingMortise(timberId, Extents3d)  // appends bbox pair to IncomingMortises xrecord
Module1.LoadIncomingMortises(timberId)             // returns Extents3d[]; empty if absent

// Stale handle correction
Module1.UpdateConnectionHandle(timberId, oldHandle, newHandle)
// Rewrites all occurrences of oldHandle -> newHandle in Connections,
// ConnectedMembers, and ConnectionEndpoints xrecords on timberId.
```

### Module1 public API additions (Phase 4 -- delta-swap)

```csharp
// Joint.Fill: BoolUnite to restore a previously subtracted void.
// No TenonCnt/MortiseCnt update; no IncomingMortises write.
// Used by DeltaSwapJoint in TimberFactory to fill the old mortise before cutting the new one.
Module1.Joint.Fill = 2

// IncomingJoints xrecord: stores full JointParams per incoming connection.
// 16-TypedValue fixed blocks keyed by giver handle.
// NOT used in the current cascade flow -- reserved for future direct-regen use.
Module1.SaveIncomingJoint(receiverId, giverHandle, JointParams)
Module1.LoadIncomingJoint(receiverId, giverHandle)  // returns JointParams (default if absent)
```

### Commands.cs NOM handshake keys (Phase 2 + Phase 3)

| Key | Written by | Read by | Content |
|---|---|---|---|
| `TRegenPending` | TimberTag | `Commands.ReadAndClearPending` | Handle + Width + Depth + JointNear + JointFar |
| `TRegenResult` | `Commands.WriteRegenResult` | TimberTag | Handle of newly drawn primary entity |
| `TRegenMap` | `Commands.WriteRegenMap` | TimberTag `ReadAndClearRegenMap` | One Text value per pair: "oldHex:newHex" |

### JointFactory (Phase 1)

```csharp
JointFactory.RegisterDefaults();       // called in Commands.Initialize()
JointFactory.Register(type, generator);// plug in a new generator at runtime
JointFactory.Create(type, JointParams) // returns ObjectId (Null for no-geometry types)
JointFactory.RequiredExtras(type)      // string[] of keys needed in JointParams.Extra
```

Generators live in `Joints\`. Working: `TenonGenerator`, `MortiseGenerator`, `ButtGenerator`.
Stubs: all others (return ObjectId.Null; implement in Phase 2 member migration).

### Commands

| Command | Description |
|---|---|
| `TDraw` | Opens TimberDraw palette |
| `TSave` | Saves current palette state to `.tproj` file |
| `TLoad` | Loads `.tproj`, restores palette |
| `TRegenTimber` | Picks a timber, prompts Width/Depth, calls TimberFactory.Regenerate() |

`TRegenTimber` works for all member types that have a `DrawContext` xrecord:
BentGirt, FloorBentGirt, PostLeft, PostRight, KPost, BentBrace, BayBrace,
KPRafterLeft/Right, KPStrutLeft/Right, KPVertStrutLeft/Right,
QPPostLeft/Right, QPRafterLeft/Right, QPStrutLeft/Right, QPUpperGirt,
HBeamLeft/Right, HBGirt, HBKpost, HPostLeft/Right, HBBayGirt,
KPTPost, KPTRafterLeft/Right, KPTStrutLeft/Right, KPTVertStrutLeft/Right,
QPTPostLeft/Right, QPTRafterLeft/Right, QPTStrutLeft/Right, QPTUpperGirt,
TrussGirt, Ridge.
EaveGirt and FloorBayGirt throw `NotImplementedException` (dual-timber draw, Phase 3 TODO).

### Adding Regenerate support for a new member type

1. At end of the member's `Draw()`:
   ```csharp
   Module1.PersistPegHandles(TimberId, PegCol);
   Module1.SaveDrawContext(TimberId, BuildContextJson());
   ```
   `BuildContextJson()` should capture all Module1 globals + member-specific fields
   needed to call `Draw()` again (e.g. postDepth, HasShoulder, etc.).

2. In `TimberFactory.cs`:
   - Add `private struct XxxContext { ... }` and `ParseXxxContext(string json)`.
   - Add `private static void RegenerateXxx(timberId, data, ctx, w, d, jn, jf)`.
     In the method body, after `s.Draw()`, pass the member's tenon ObjectId fields to
     `ApplyJointTypes()` so joint-type geometry changes work:
     ```csharp
     // Both-end tenon (near=left/down, far=right/up):
     ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
     // Single far-end tenon:
     ApplyJointTypes(s.TimberId, jn, jf, farTenonId: s.Tenon);
     // No tenons (post, truss girt):
     ApplyJointTypes(s.TimberId, jn, jf);
     ```
   - Add `case "TypeName": RegenerateXxx(...); break;` to the `Regenerate()` switch.

3. Build and test with `TRegenTimber`.

## Common Pitfalls

- **Non-ASCII in source**: Any Unicode character in a `.cs` file (comments, strings, anywhere) will break the ANSI-mode compiler. Use `x` not `x`, `-` not bullet, `->` not arrow, etc.
- **Variable scope in dual joinery blocks**: EaveGirt and HBBayGirt each have two `if (Module1.HasJoinery)` blocks. Declare peg variables (`r`, `ps`, `maxPegPos`) in each block independently.
- **Assembly name vs command**: The assembly is `TimberDraw.dll` and namespace is `TimberDraw`. The AutoCAD command must be `TDraw` -- using `TimberDraw` as the command name conflicts with the assembly and will not register.
- **`new()` syntax**: Requires C# 9.0+. The project is set to 9.0. Do not revert to 8.0 without replacing all `new()` calls with explicit types.
- **Peg bounds**: Always use the `maxPegPos` guard when placing pegs. Never unconditionally place all 3 pegs -- for typical 6-10" members only 1-2 fit within TFG clearances.
- **ObjectARX hint paths**: References use absolute paths (`C:\Autodesk\ObjectARX_for_AutoCAD_2020_Win_64_bit\inc\`). Relative paths in the old `.csproj` did not resolve correctly from this directory depth.
- **Cross-bent queue completeness**: When adding a new bent type to UserControl1, ALL five pending queues must be cleared after Draw() -- not just the post pair. Truss types clear all five unconditionally. Non-truss types consume into the appropriate member before clearing.
- **Ridge far-end has TWO housings**: Ridge.cs draws `Housing2Id` AND `Housing2aId` at the far end (matching the near-end `Housing1Id`/`Housing1aId` pair). For HB/KPB, enqueue `Housing2Id` to `PendingKPostMortises` and delete `Housing2aId`. For QPB, enqueue both to the rafter queues. For truss types, delete both immediately.
- **Purlins requires `using System.Collections.Generic`**: The `ExtensionLeftFarList`/`ExtensionRightFarList` fields are `List<(ObjectId, ObjectId)>`. Without the using directive the build will fail with CS0246.
- **Far-end tenon IDs must be captured**: Several draw calls inside `if (Module1.HasJoinery)` blocks return ObjectIds that must be stored in fields for later queuing. EaveGirt lines 85 and 148 (`TenonLeftFarId`/`TenonRightFarId`) are examples. Silently discarding the return value leaves the far-end tenon orphaned -- it draws but is never subtracted from the next bent's timber.
- **JointNear/JointFar must never be empty**: Every `DrawElement`/`DrawBrace` call for a structural timber must pass `jointNear:` and `jointFar:` named arguments. Omitting them leaves the xdata fields blank, breaking the regen dialog pre-population and the joint-type geometry update in `ApplyJointTypes()`. Joinery sub-entities (Tenon, Mortise, Housing) intentionally pass `""` -- only the main timber solid call must supply values.
