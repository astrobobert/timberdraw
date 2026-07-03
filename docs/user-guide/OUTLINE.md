# TimberDraw User Guide -- Outline

Working outline for the end-user guide. Chapters follow the framer's workflow: design -> freeze ->
edit -> joinery -> shop output -> laser. Sources of truth while writing: `GLOSSARY.md` (all trade
terms and parameter names) and the command code itself -- never document from recollection.
Items marked **(verify)** need a code/UI check at writing time.

**Audience:** timber framers who know basic AutoCAD (open a drawing, pick points, orbit). No
programming knowledge assumed. New-collaborator developers get CLAUDE.md/README, not this guide.

**Style rules for the guide itself:**
- Diagram-led: every concept gets a figure before (or instead of) a paragraph. Short sections,
  bulleted steps, one idea per paragraph.
- Glossary terms bold on first use per chapter, linked to Appendix B. Use the canonical parameter
  names (Seat, ShoulderTop/Bottom, Thickness, Offset, Length) everywhere.
- One worked example runs through the whole guide: a small 2-bent king-post frame (the quick-start
  frame), so every screenshot is the same recognizable building.
- Colorblind-safe figures: never red-vs-green; annotate in blue/yellow (matches the app's own
  ground-column convention).
- Dimensions in inches, framer's fractions (1/2", not 0.5").

---

## Part 1 -- Getting Started

### 1. What TimberDraw Is
- The suite in one picture: TimberDraw (design + cut sheets in AutoCAD) -> `.tsj` files ->
  TimberScribe (Pi + laser scribes the timber). Figure: pipeline diagram.
- What you get out: a 3D frame model, BOM/cut list, shop drawings, laser scribe jobs.
- What it is not (yet): engineering/load calcs, CNC output.

### 2. Installation
- Requirements: AutoCAD 2020+ (verify: which versions actually load net48 plugins), Windows.
- APPLOAD / NETLOAD the DLL; add to Startup Suite. `TVer` to confirm the loaded build.
- NETLOAD cannot hot-swap: reopen AutoCAD to pick up a new DLL.
- Where files land: scribe output folder (defaults next to the drawing), shop layouts in the DWG.

### 3. Concepts: How TimberDraw Thinks
The one required reading chapter. Figures over prose.
- **The timber is the truth.** Managed solids carry their own geometry, joinery, and identity;
  there is no side database. Don't grip-edit managed solids -- use the verbs.
- **Recipe -> skeleton -> freeze.** The frame spec is a generator: it emits the skeleton, then
  `TFreeze` ends the parametric phase (one-way). After the break every timber is edited the same
  way, skeleton or free.
- **The grid is an address, not a driver.** Bent numbers + wall letters (skip I/O) locate and
  label; they never move timbers.
- **Three IDs per timber:** location label (where it installs), cut-mark (identical sticks share
  it -- the buy list), production number. Figure: one timber with all three called out.
- Reference faces RS1-RS4 + ends 5-6; the reference face/arris as layout datum.
- Model basis: the frame stands Z-up in WCS; UCS is placement-only.

### 4. Quick Start: A Frame in Fifteen Minutes
The whole pipeline once, no options explained -- forward pointers to each chapter.
1. `TDraw` -> seed a 2-bent king-post frame -> Draw.
2. `TFreeze` -- lock it in.
3. `TJointAll` -- cut the girt-to-post joinery in one pass.
4. `TBom` -- the cut list appears.
5. `TShop` -- bent/wall/plan shop drawings.
6. `TScribeAll` -- `.tsj` files ready for the laser.
Screenshot per step; end with the burned-timber photo if we have one.

---

## Part 2 -- Designing a Frame (the Recipe)

### 5. The Frame Editor (`TDraw`)
- The four panes: tree, properties, description, button row. Figure: annotated palette screenshot.
- The instance model: Bents x Walls (A-E lettering, ridge line) x Bays; member toggles.
- The five bent types (KingPost, QueenPost, HammerBeam, KingPostTruss, QueenPostTruss) --
  one elevation figure each, naming the members with glossary terms.
- Frame-level params: span, eave height, pitch, frame length, bent spacing/bay schedule.
- Recipe params that survive regeneration: Girt Drop, per-bay floor-girt Height, eave girt height,
  common-rafter tails (plumb/square cut), braces-follow-their-head. Table: param -> what it moves.
- Roof: commons vs purlins, spacing modes, sizes.
- Draw / re-Draw semantics: regenerating replaces the skeleton until the freeze; hand edits to a
  pre-freeze skeleton are clobbered (that's what the freeze is for).
- `TRoughIn` as the command-line equivalent (verify: present as primary path or alternate).

### 6. The Break: `TFreeze`
- Why one-way: the recipe stops being authoritative; the timbers carry on as truth.
- What is stored (seed params on the frame group) and what the gate refuses afterward.
- When to freeze: as soon as the skeleton is right and before hand-cut joinery/infill.

### 7. The Structural Grid and Labels
- `TGrid`: derived from the drawing, redrawn on demand, flat under the frame.
- Grid conventions: numbers = bents (intermediate 1.1), letters = walls (skip I/O, intermediate
  A.1); ground columns yellow, others blue.
- Label grammar (one figure: a labeled frame axonometric): digit-first = bent members (`P-2A`),
  letter-first = wall/bay members (`EG-B-I`), bay ranges, Dn/Up levels for floor/tie girt pairs,
  brace group symbols (*, **), commons/purlins numbered per bay.
- `TAssign`: adding free timbers to a group so they get addresses.
- `TRelabel`: retrofitting Dn/Up onto a frame emitted before the convention existed.

---

## Part 3 -- Editing Timbers

### 8. The Assembly Palette (`TPanel`) and the Editor Verbs
Verb-per-section, each: what it asks you to pick, what it does, one before/after figure.
- `TPlace` -- place a timber (sticky section sizes, pick extrusion direction).
- UCS presets: `TUcsPlan` / `TUcsBent` / `TUcsWall` -- orient placement without UCS gymnastics.
- `TSpan` -- connect two timbers (finds the facing faces, fills the gap).
- `TJoin` -- connect two picked faces (square filler or mitered knee).
- `TFit` -- trim/extend one end onto a target face.
- `TSection` -- re-section in place (W x D).
- `TScarf` -- splice a timber in two (stores the scarf interface).
- `TJoist` -- a row of floor joists in a wall/bay.
- Move / rotate / delete managed timbers (verify: current move/rotate surface -- TPanel buttons).
- `TScan` -- rescan face coincidence; what the node markers mean.
- `TBrowse` -- the frame browser: list, filter, select-to-zoom.

---

## Part 4 -- Joinery

### 9. Joinery Concepts
Digest of GLOSSARY B-E, figure-heavy; the glossary itself is Appendix B.
- Joint anatomy: cheek, shoulder, relish, housing, seat, heel. One exploded M&T figure.
- How connections are organized: contact topology + elements; the same engine cuts many
  role-pairs (why a brace-into-post and a strut-into-rafter feel identical to use).
- The canonical parameters, one diagram each: Seat; ShoulderTop/Bottom (0 = flush);
  Thickness + Offset (0 = centered; pushed to a face = barefaced); Length; peg layout
  (Count, Diameter, Setback, Spacing, full vs blind bore).
- Pegs bore the post only; draw-boring is done by hand in the field (recommended practice note).

### 10. Cutting Joints
- The Joints pane (`TPanel` -> Joints tab): `TJoinPick` a member + its host, edit the element
  stack, Apply (`TJoinApply`); re-apply re-cuts the SAME joint (idempotent, by joint id).
- The connection catalog, one section per connection (figure + param table + the Del command),
  mirroring GLOSSARY D:
  - Girt -> post: `TJoint` (tenon + housing + pegs), `TJointAll` batch over the whole frame,
    `TJointDel`.
  - Brace -> girt/post: `TBrace` (1.5" barefaced default), `TBraceDel`.
  - Strut/V-strut -> rafter/post/king post: `TStrut`, `TStrutDel`.
  - Rafter foot -> post: `TRafterFoot`, `TRafterFootDel`.
  - Rafter head -> king post: `TRafterHead`, `TRafterHeadDel`.
  - Ridge -> king post: `TRidge`, `TRidgeDel`.
  - Principal rafter -> ridge: `TRidgeRafter`, `TRidgeRafterDel` (verify: scope vs TCommonRidge).
  - Common rafter -> ridge: `TCommonRidge`, `TCommonRidgeDel`.
  - Common rafter -> eave girt (birdsmouth): `TCommonEave`, `TCommonEaveDel`.
  - Purlin -> rafter (dovetail housing): `TPurlin`, `TPurlinDel`.
  - Queen-post rafter seat: `TQPRafter`, `TQPRafterDel` (verify: exact contact).
  - Beam -> beam scarf: `TScarf`.
- Deleting vs re-cutting; what happens to pegs when the joint goes.

---

## Part 5 -- Getting It Built (Shop Output)

### 11. The Cut List (`TBom`)
- The grid: per-timber rows, sortable; select rows -> solids highlight in the model.
- Reading it: measured overall length includes projecting tenons; joinery tallies; how identical
  cut-marks group into the buy list.

### 12. Shop Drawings (`TShop`)
- One map per bent, per wall, plus plan; every stick as a labeled box in context.
- The paper-space layouts ('TM Shop', 3/8" = 1'-0"); `TShopClear` to remove.
- Suggested print/plot workflow (verify: plot styles worth recommending).

### 13. Scribe Export (`TScribe` / `TScribeAll`)
- What gets burned on each face, one annotated preview figure: profile linework,
  cut-to-length lines at BOTH ends of every face, depth labels (pocket/housing), saw-angle
  (bevel) labels on undercut walls, blind-peg 'B' centered in the bore, the location label.
- The datum rule: which face is RS1, where the origin sits (upper-left), why the framer squares
  the stock and marks the reference face first.
- `TScribe` (selection) vs `TScribeAll` (whole frame, clears the folder); output folder prompt;
  file naming (`<label>_faceN.tsj`); identical braces collapse to one drawing set.
- 1/2" fixed text: labels never shrink; what to do when a face is crowded.
- `TScribeProbe`: when a label looks wrong, probe the timber and read the per-face verdicts.

### 14. TimberScribe: Burning the Timber
Bridge chapter -- full detail lives in the timberscribe repo's own guide.
- Loading `.tsj` files onto the Pi; the job list.
- Aligning the laser to the reference face + arris; datum = upper-left.
- Running a burn; face order (RS1-4); safety notes.
- Pointer to TSJ_SPEC.md for the file format (pending doc, see repo plan).

---

## Part 6 -- Appendices

### A. Command Reference
Every command, one line each, grouped: Frame design / Grid + labels / Editor verbs / Joinery /
Output / Utility (`TVer`, `TPickFace`, `TScribeProbe`) / Parked-legacy (`TFrameFlat`,
`TDrawLegacy`, `TRegenTimber`, `TFrame`/`TFrameQP`/`TFrameHB`/`TFrameKPT`/`TFrameQPT`,
`TFrameSave`/`TFrameLoad`, `TSave`/`TLoad`) -- legacy marked "not for new drawings".

### B. Glossary
Generated from `GLOSSARY.md` (single source of truth -- do not fork the content; transclude or
copy at build time).

### C. The .tsj File Format
Pointer to TSJ_SPEC.md in the timberscribe repo. One paragraph: JSON, one file per face,
origin upper-left, inches.

### D. Troubleshooting
- "TRoughIn: frame A is frozen" -- the gate is working; re-seed a fresh frame to go parametric.
- Stale build after rebuild -- `TVer`; reopen AutoCAD (NETLOAD can't hot-swap).
- "no scribe marks (plain stick) -- skipped" -- expected for uncut sticks? No: end lines burn on
  every face now; explain when a timber is genuinely skipped. (verify current behavior)
- Drawing won't save after joinery (SOLIDHIST) -- shouldn't happen anymore; what to report.
- Labels missing Dn/Up on an old frame -> `TRelabel`.

---

## Open decisions (need Robert's call before drafting)

1. **Format/toolchain:** markdown chapters in `docs/user-guide/` (one file per chapter, images in
   `docs/user-guide/img/`) rendered by GitHub as-is? Or single-page HTML/PDF for the shop?
   Recommendation: markdown per chapter now; PDF export later.
2. **The worked example frame:** exact seed for the quick-start frame (proposal: 2 king-post
   bents, 16' span, 12' eave, 8:12, one bay) so screenshots are reproducible.
3. **Screenshots:** who captures (AutoCAD theme, background color, resolution standard)?
   Proposal: light background, consistent SE isometric, blue/yellow annotation only.
4. **Scope of ch.14:** thin bridge (recommended) vs full TimberScribe operation manual here.
5. **Writing order proposal:** ch.3 Concepts -> ch.4 Quick Start -> Part 5 (output chapters,
   freshest in memory) -> Part 4 joinery -> Parts 2-3 -> appendices last.
