# TimberDraw Root .cs Files — Summary

The bent/bay member classes formerly at the root (PostLeft, BentGirt, EaveGirt, Ridge, Purlins,
Commons, ...) now live in `Bent\` and `Bay\` — they are the legacy generator's internals and are
summarized in CLAUDE.md. This file covers what remains at the project root: command registration,
global state, and the palette UI controls.

---

### Commands.cs
The plugin **entry point**. Registers every AutoCAD command and owns the palettes: `TDraw` (frame
tree editor), `TRoughIn` (generate from the spec), `TFreeze` (one-way freeze gate), `TGrid`
(structural grid), `TPanel` (assembly palette), `TBrowse` (frame browser), `TBom` (BOM palette),
`TVer` (build stamp), `TFrameSave`/`TFrameLoad`, and the parked legacy commands (`TFrameFlat`,
`TDrawLegacy`, `TRegenTimber`, `TFrame*`).

---

### Module1.cs
The **legacy global state and utility hub** for the parametric generator path: shared bent
parameters (`Span`, `EaveHt`, `Pitch`, `Beta`, `Make3D`, ...), `DrawElement()`, `DrawPeg()`,
`DrawBrace()`, `AddJoint()`/`DeleteJoint()`, `AddConnection()`, `SetXdata()`/`GetXdata()`, the
`DataStructure` xdata schema, the cross-bent pending-mortise queues, and geometry helpers
(`arabic2roman()`, `PolarPoint()`, `AtPoint()`).

---

### UserControl1.cs
The **legacy flat palette** (opened by `TDrawLegacy`): wires Windows Forms controls to `Module1`
globals and orchestrates the old full-bent drawing sequence including bay members and cross-bent
joinery wiring.

---

### FrameTreeControl.cs
The **frame tree editor** hosted by `TDraw`. The FrameSpec is the source of truth: the tree holds
Bents and Bays (right-click to add), the property pane edits the selected item, member checkboxes
toggle timbers, and the Draw button regenerates the skeleton (refused once frozen).

---

### PropertyPane.cs
A lightweight, **fully-ordered property panel** that replaces the stock PropertyGrid: renders
PropertyDescriptors in exact order with section headers, separators, per-row styling, and
type-appropriate editors; values go through each descriptor's converter (unit input/display).

---

### FrameControl.cs
Frame-shaped palette for the early TFrame node/edge model (KingPost only). Persists through
`Settings.Default` and drives `Commands.DrawFrame`/`SaveFrame`/`LoadFrame`. Superseded by the
tree editor for day-to-day use.

---

### TimberPaletteControl.cs
The **assembly palette** (`TPanel`). Sections shown as a tree grouped by member type with each
cross-section (W x D) as a leaf; selecting a leaf sets the sticky `ManagedSection`. Verb buttons
fire the managed commands; orientation buttons fire the UCS presets.

---

### JoinPaletteControl.cs
The **Joints pane**: pick a timber pair (`TJoinPick`), choose a connection type, and edit joinery
params on one stable grid listing every element + param across all connection types (unavailable
rows grayed). The real joint re-cuts live as values change; each param carries a glossary tooltip.

---

### JointGlossary.cs
Plain-language **tooltips for the Joints pane**, mirroring GLOSSARY.md sections C (joint parts) and
E (canonical params). Pure UI text — keep in step with GLOSSARY.md, the single source of truth.

---

### BomGridControl.cs
The **BOM palette** (`TBom`): a sortable, read-only DataGridView of the per-timber piece tally.
Selecting rows highlights the matching solids in model space; Refresh re-reads the model; Export
writes CSV.

---

### TimberFactory.cs
The **legacy parametric regeneration dispatcher** (`TRegenTimber` path): reads a timber's stored
DrawContext + xdata, erases and redraws with new dimensions, and cascades mortise re-cuts into
connected members. Applies to legacy-pipeline drawings only — managed timbers are edited with the
editor verbs instead.

---

### ProjectFile.cs
**`TSave` and `TLoad`** for the legacy `BentProject.json`/`.tproj` project file format (palette
state snapshot). The managed path persists frame specs via `TFrameSave`/`TFrameLoad` and stores
everything else on the timbers themselves.
