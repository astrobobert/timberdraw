# TimberDraw Root .cs Files — Summary

---

### Commands.cs
The plugin **entry point**. Registers the `TDraw` AutoCAD command, creates a `PaletteSet` docked panel on first call, adds `UserControl1` to it, and saves settings on plugin unload.

---

### Module1.cs
The **global state and utility hub** — the most important file in the project. Contains:
- All shared bent parameters (`Span`, `EaveHt`, `Pitch`, `Beta`, `Make3D`, `HasJoinery`, etc.)
- `DrawElement()` — draws a polyline profile and optionally extrudes it to a 3D solid
- `DrawPeg()` — draws a cylindrical peg on the "pegs" layer
- `DrawBrace()` — draws a brace using a 2D polyline with bulge (curved corner)
- `AddJoint()` / `DeleteJoint()` — boolean union (tenon) or subtraction (mortise) on 3D solids
- `AddConnection()` — stores a linked-member relationship in entity extension dictionary xdata
- `SetXdata()` / `GetXdata()` — reads/writes `Type`, `BentNumber`, `Designation`, `Size`, tenon/mortise/peg counts to each entity's extension dictionary
- `DataStructure` — the xdata schema class
- `JointType` enum — full vocabulary: Tenon, Mortise, Butt, Dovetail, Birdmouth, Scarf, Spline (used for future TimberScribe export)
- `arabic2roman()`, `MaxDeflection()`, `PolarPoint()`, `AtPoint()` — geometry and utility helpers

---

### PostLeft.cs / PostRight.cs
**Wall post** members. Draw a vertical post profile from grade to eave height, with optional shouldered seats (1" setback notches) at the bent girt and floor girt elevations. The shoulder cuts are mirror images of each other. Both expose `AddMortise()` to receive tenons from connecting bay members.

---

### BentGirt.cs
The **horizontal tie beam** across the bent at eave height. Draws a rectangular profile spanning between the two posts (minus post depth), with optional 1" shoulder housings. Draws left and right tenons (4" long, 2" wide) and places TFG-standard pegs at both ends, guarded by `maxPegPos`. Exposes `AddMortise()`.

---

### BentBrace.cs
The **diagonal knee brace** within the bent plane. Uses a 45 degree geometry (`SinBeta = sqrt(2)/2`) and `DrawBrace()` with a small bulge radius at the corner. Draws two tenons (up and down) and one peg each. Respects `OffsetType` (Back/Centered/Front) in 3D to position itself within the post width.

---

### BayBrace.cs
The **diagonal brace across a bay** (the 3D direction). Nearly identical geometry to `BentBrace`, but oriented in the bay axis. Accepts explicit `Peg1Z`/`Peg2Z`/`Peg1Length`/`Peg2Length` for precise peg placement into the connecting posts/girts. Also supports `XAngle` rotation for the four brace positions (two bents x two sides).

---

### EaveGirt.cs
The **longitudinal girt at eave height** running bent-to-bent. One of the more complex members -- draws two separate timbers (left-side and right-side of the bent, with the roof slope notch cut into the top edge). Each side has its own tenon and up to 3 pegs placed symmetrically about the beam center (`center +/- ps`), guarded by `[minPegY, maxPegY]`. Has a `Side` enum and `AddMortise(id, side)` to receive eave brace mortises into the correct half.

---

### FloorBentGirt.cs
The **floor-level girt in the bent plane**. Structurally identical to `BentGirt` but positioned at `Height` rather than eave height. Same shoulder logic, same 4"-long tenon geometry, same TFG peg pattern. Draws pegs at both ends with the same `maxPegPos` guard.

---

### FloorBayGirt.cs
The **floor-level girt running across bays** (longitudinal, like `EaveGirt` but at floor level). Draws two timbers -- one on the left post face and one on the right -- each with two tenons (near and far bay ends). `RightBentNumber()` looks up the correct wall letter based on truss type (different member counts per bent type). Exposes `AddMortise(id, Side)`.

---

### TrussGirt.cs
The **bottom chord / tie beam for truss bents** (King Post Truss and Queen Post Truss). Spans full width at eave height, with notched birdmouth cuts on both ends to receive the rafter tails at the correct plumb angle. No joinery (pegs/tenons) -- `TenonWidth` is commented out. Exposes `AddMortise()`.

---

### Ridge.cs
The **ridge beam** at the apex of the roof. Draws the ridge profile (hexagonal -- rectangular with angled top cuts matching roof pitch). In joinery mode, creates:
- Two plumb-cut **tenons** (near and far bay ends)
- Two **mortise** slots (`Mortise1Id` + `Mortise1aId` for king post; `Mortise2Id` for far end) that the King Post or Queen Post rafters cut into

Exposes `AddMortise()` to receive ridge brace tenons.

---

### Purlins.cs
**Roof purlins** -- secondary roof framing members running parallel to the ridge, spaced 48" on-center along the rafter slope. Uses `PolarPoint()` to walk down the rafter length in both directions from the ridge, drawing one purlin profile at each station. In joinery mode draws a dovetail tenon at the near-bent face and a simple tenon at the far face, collecting them into `TenonLeftCol`/`TenonRightCol` for the calling code to wire into rafter mortises.
---

### Commons.cs
**Common rafters** -- the intermediate rafters between bents, spaced to evenly fill each bay. Calculates spacing from bay width and rafter count. Draws pairs of rafters (left slope and right slope) at each Z station. Handles two cases: with ridge (rafters butt into ridge sides) and without ridge (rafters meet at the apex). In joinery mode draws eave-seat housings at the bottom and ridge-lap housings at the top, wired into `TenonLeft/RightDownCol` / `TenonLeft/RightUpCol`. The `IDisposable` pattern is correctly implemented here (unlike `Purlins`).

---

### UserControl1.cs
The **main UI controller** (~2,769 lines). Handles all Windows Forms interactions: loading settings, wiring UI controls to `Module1` globals, and the `ButtonPickStartPoint_Click` handler that orchestrates the full drawing sequence -- instantiates the selected bent type, sets all its properties from settings, calls `Draw()`, then draws all bay members (ridge, braces, eave girts, purlins, commons, floor girts) and wires joinery connections between them.

---

### ProjectFile.cs
**`TSave` and `TLoad` commands** for the `BentProject.json` project file format.
`TSave` snapshots the current palette state (all bent and bay parameters) into a
`.tproj` file. `TLoad` reads it back, restores all settings, and reopens the
`TDraw` palette so the UI reflects the loaded values. Data model:
`BentProjectFile` contains `Bents[]` (one entry per bent) and `Bays[]` (one entry
per bay between adjacent bents). Serialised with `System.Text.Json`.
