# 13. Scribe Export (`TScribe` / `TScribeAll`)

The last step in CAD: turn each timber into `.tsj` burn files — one per face —
that the TimberScribe laser head follows. What the laser draws on the wood is
what you'd otherwise lay out with tape, square, and pencil.

---

## 13.1 What gets burned on a face

> **Figure 13-1 — One girt face, annotated.**
> *[capture: preview of a girt's RS1 .tsj (or the TimberScribe web preview) for
> a girt with a mortise and pegs; blue callouts on each mark type listed
> below.]*

Each face drawing carries:

- **Profile linework** — the joinery outlines as seen on that face: mortises,
  tenon shoulders, housings, notches.
- **Cut-to-length lines at BOTH ends, on every face** — even a plain face gets
  its end lines, so the stick can be cut to length from any side.
- **Depth labels** — pocket/housing depths, so the framer knows how deep without
  going back to the drawings.
- **Saw-angle (bevel) labels** on undercut walls — where a mortise wall isn't
  square to the face.
- **A blind-peg 'B'** centered in any peg bore that doesn't go through.
- **The timber's location label**, so the stick identifies itself.

Text burns at a fixed **1/2" height** — labels never shrink to fit. On a crowded
face a label anchors in the nearest clear spot within its feature's visible
region.

## 13.2 The datum: RS1 and the upper-left origin

Every face's coordinate origin is its **upper-left corner** as the laser sees
it, and the face set is numbered from the **reference face RS1** (Chapter 3.5).
That is why shop practice is: square the stock, choose and mark the reference
face and **reference arris** first, then align the laser to them. If the datum
is right, every mark on all four faces lands right.

## 13.3 Running an export

- **`TScribeAll`** — the whole frame. Prompts for an output folder (default:
  a `Scribe` folder next to the drawing) and **clears prior `.tsj` files from
  it first**, so the folder always matches the model exactly.
- **`TScribe`** — just the timbers you select. Leaves the rest of the folder
  intact — the "I re-cut one joint" workflow.

Files are named `<label>_faceN.tsj` — e.g. `P-2A_face1.tsj` through
`_face4.tsj`. Only faces that carry marks are written.

**Identical repeats collapse to one set.** Repetitive families (braces, joists,
commons, purlins) export one drawing set per unique geometry, named by family
and section with the count in the stem — `Brace_4x5_x12_face1.tsj` means *cut
twelve of these*. The command line echoes each timber exported, its faces, mark
counts, and any collapsed repeats.

> **Figure 13-2 — The command-line echo and the resulting folder.**
> *[capture: AutoCAD text window after TScribeAll on the quick-start frame,
> plus the Scribe folder in Explorer showing the file naming.]*

## 13.4 When a mark looks wrong: `TScribeProbe`

`TScribeProbe` selects one timber and prints, per face, exactly what the
annotator decided and why — which features hit the face, where each label
anchored, what was skipped. Read the verdicts before assuming the export is
wrong; usually the timber's joinery says exactly what the probe reports.

---

*Next: [Chapter 14 — TimberScribe: Burning the Timber](14-burning.md).*
