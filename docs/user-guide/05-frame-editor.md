# 5. The Frame Editor (`TDraw`)

The frame editor is where the **recipe** lives (Chapter 3.2): the structure of
the frame as bents, walls, and bays, with member sizes and toggles. Press
**Draw** and the generator emits the skeleton; change anything and Draw again —
until the freeze.

---

## 5.1 The four panes

> **Figure 5-1 — The frame editor, annotated.**
> *[capture: TDraw palette with the quick-start frame; blue callouts numbering
> the four panes: 1 tree, 2 properties, 3 description, 4 button row.]*

`TDraw` opens a palette with four panes:

1. **The tree** — the frame's structure: bents and bays alternating, each with
   its member folders.
2. **The properties pane** — every editable value for whatever is selected in
   the tree, in worked order with section headers. Distances accept framer
   input (`12'`, `12'-6"`, `9 1/2`).
3. **The description pane** — what the selected item and its parameters mean.
4. **The button row** — Draw, Freeze, and the template Save / Save As / Load.

The palette opens with a **fresh two-bent king-post seed every time** (and on
every drawing switch). To keep a design across sessions, save it as a named
template (below) — the drawn frame itself is never lost; only the palette
restarts.

## 5.2 Building the structure

- **Right-click in the tree** to add: **Add Bent** / **Add Bay** (they
  alternate; a bay may lead or trail the frame).
- Select a bent and set its **Bent Type** — KingPost, QueenPost, HammerBeam,
  KingPostTruss, QueenPostTruss. Setting the type fills the bent's folder with
  all of its timbers as **checkboxes**, all checked; uncheck a timber to drop
  it from the frame.
- Select a bay and set its **Roof Type** and separation.

> **Figure 5-2 — The five bent types.**
> *[capture or diagram: five small elevations side by side, each labeled, with
> the members named in glossary terms (post, tie girt, king post, queen posts,
> straining beam, hammer beam, hammer post, collar).]*

**Walls.** The frame is modeled as lettered longitudinal wall lines (A, B, C,
... — the ridge runs on the center line), and each wall line owns its member
catalog. The interior wall count and roles follow the bent type — a hammer-beam
frame carries more lines than a king-post frame. You mostly notice walls when
assigning free timbers (Chapter 7) and reading labels.

## 5.3 Parameters that survive re-Draw

Frame-level: **Span**, **Eave Height**, **Pitch**, frame length / bent
separations. Member-level (the recipe params — set them once, they hold through
every re-Draw):

| Parameter | What it moves |
|---|---|
| **Girt Drop** | Lowers the tie girt below the eave (6" minimum) |
| **Floor girt Height** (per bay) | The floor girt's elevation in that bay |
| **Eave girt Height** | The eave girt's elevation on its wall |
| **Common tail + Tail Cut** | Rafter tail length past the eave, plumb or square cut |
| **Braces follow their head** | Braces track the girt they brace when it moves |

Roof framing per bay: **commons or purlins**, count or spacing mode, sizes.

**Per-type defaults:** with a member selected, its right-click **Save as
default** stores that member type's sizes and checkboxes — new elements of the
same type seed from it.

## 5.4 Draw, re-Draw, and templates

**Draw** emits the whole frame as managed timbers, standing Z-up, at the
current UCS origin — with the structural grid beneath and every stick labeled.
Drawing again **replaces** this frame's timbers (other frames and unmanaged
solids are untouched). Hand edits to a pre-freeze skeleton do not survive a
re-Draw; that is what the freeze is for (Chapter 6).

**Save / Save As / Load** manage named `.framespec` templates — your barn
starter, your saltbox. Loading one replaces the palette's spec; Draw makes it
real.

`TRoughIn` is the command-line alternate: it emits a single bent of the type
set in the palette's rough-in setting, prompting for a base point. The tree
editor is the primary path.

---

*Next: [Chapter 6 — The Break: `TFreeze`](06-freeze.md).*
