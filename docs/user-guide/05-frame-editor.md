# 5. The Frame Editor (`TDraw`)

The frame editor is where the **recipe** lives (Chapter 3.2): the structure of
the frame as bents, walls, and bays, with member sizes and toggles. Press
**Generate Frame** and the generator emits the skeleton; change anything and
Generate again — until the freeze.

---

## 5.1 The Frame tab

![Figure 5-1](images/Figure%205-1.png)

> **Figure 5-1 — The frame editor, annotated.**

<!-- capture: Frame tab with the quick-start frame; blue callouts numbering
the three panes: 1 tree, 2 properties, 3 action bar. -->

`TDraw` opens the TimberDraw palette on its **Frame** tab (the same palette
carries Assembly, Joints, Browser, and Output — each chapter lands on its own
tab). The Frame tab is three panes:

1. **The tree** — the frame's structure: bents and bays alternating, each with
   its member folders.
2. **The properties pane** — every editable value for whatever is selected in
   the tree, in worked order with section headers. Distances accept framer
   input (`12'`, `12'-6"`, `9 1/2`).
3. **The action bar** — **Generate Frame** on its own row, the template row
   (**New / Save / Save As / Load**), and **Set Default / Freeze / Redraw
   Grid**.

The tree **recalls the open drawing's own frame**: every Draw (and the
Freeze) stamps the recipe into the drawing, so opening or switching to a
drawing whose frame came from the tree refills the palette with *that*
frame. A drawing with no stamped frame (never drawn here, or drawn before
July 2026) opens **empty** — a frame then starts with **New**, a loaded
template, or a right-click **Add Bent** on the frame node. To carry a design
between projects, save it as a named template (below).

## 5.2 Building the structure

- **Right-click to grow.** An empty tree's frame node offers **Add Bent** /
  **Add Wall** (the first elements); after that, right-click a bent or wall
  for **Insert Before / After** and **Remove**. The bays between bents follow
  automatically.
- Select a bent and set its **Bent Type** — KingPost, QueenPost, HammerBeam,
  KingPostTruss, QueenPostTruss. Setting the type fills the bent's folder with
  all of its timbers as **checkboxes**, all checked; uncheck a timber to drop
  it from the frame.
- Select a bay and set its **Roof Type** and separation.

![Figure 5-2](images/Figure%205-2.png)

> **Figure 5-2 — The five bent types.**
<!-- capture or diagram: five small elevations side by side, each labeled, with
the members named in glossary terms (post, tie girt, king post, queen posts,
straining beam, hammer beam, hammer post, collar). -->

**Walls.** The frame is modeled as lettered longitudinal wall lines (A, B, C,
... — the ridge runs on the center line), and each wall line owns its member
catalog. The interior wall count and roles follow the bent type — a hammer-beam
frame carries more lines than a king-post frame. You mostly notice walls when
assigning free timbers (Chapter 7) and reading labels.

## 5.3 Parameters that survive re-Generate

Frame-level: **Span**, **Eave Height**, **Pitch** (shown as rise:12); the
frame's length is the sum of the per-bent **Separation** values. Member-level
(the recipe params — set them once, they hold through every re-Generate):

| Parameter | What it moves |
|---|---|
| **Girt Drop** | Lowers the tie girt below the eave (6" minimum) |
| **Floor girt Height** (per bay) | The floor girt's elevation in that bay |
| **Eave girt Height** | The eave girt's elevation on its wall |
| **Common tail + Tail Cut** | Rafter tail length past the eave, plumb or square cut |
| **Braces follow their head** | Braces track the girt they brace when it moves |
| **Sill Height** | The sill's TOP elevation — 0 sits it right under the post feet, negative drops it deeper |

Roof framing per bay: **commons or purlins**, count or spacing mode, sizes.

**Sills and summers ship OFF.** Every post-bearing bent and eave bay carries a
**Sill** leaf, and the center line's bays a **Summer** leaf (the mid-span floor
carrier, top flush with the floor girts) — tick the checkbox to add them. Post
feet stay at elevation zero; sills hang below that datum, and the post feet
stub-tenon down into them (`TJointAll` cuts those, Chapter 10).

**Braces solve for two of three.** A brace's rows are **Foot / Head / Angle**
with a checkbox on each label: the two checked rows are your inputs, the third
goes read-only and derives (check a third and the oldest drops out — the same
mechanic as the Assembly tab's Brace spec). Length is always reported.

**Per-type defaults:** with a member selected, its right-click **Save as
default** stores that member type's sizes and checkboxes — new elements of the
same type seed from it.

## 5.4 Generate, re-Generate, and templates

**One frame per drawing** is the convention: a drawing holds one frame, whose
name is the tree root's *Name*. Draw warns if the drawing already carries a
frame under a different name (renaming the spec and Drawing again would add a
second frame beside the first, not rename it). Start the next frame in a new
drawing — or with **New** here.

**Generate Frame** emits the whole frame as managed timbers, standing Z-up, at
the current UCS origin — with the structural grid beneath and every stick
labeled. Generating again **replaces this frame's skeleton only**: hand-placed
timbers (joists, summers, braces, anything from the editor verbs) survive a
re-Generate, assigned to the frame or not — the generator erases only what it
emitted itself. **Your joinery survives too**: before the old skeleton is
erased, every joint's recipe is harvested, and after the new skeleton is
emitted each joint is re-cut onto it automatically — matched by role and
position, with the member's **label** as the rescue when a parameter change
relocates the skeleton (a new eave height moves every girt; labels don't
change unless the member count does). It works after an inserted bent
renumbers the labels *and* after a param change moves the members, and
custom per-joint edits (extra pegs, an odd tenon) replay exactly. Joints to
surviving free timbers re-attach in the same pass; joints the replay could not
confidently restore (a member that moved too far, or two equally near
candidates) are **reported, never guessed** — heal those with `TJointSync`
(Chapter 10.3) or re-cut with `TJointAll`. Replay only restores joints you had
already cut; it never creates new ones. Shape edits **to skeleton members
themselves** survive too: `TProfile` / `TFit` / `TSection` / `TScarf` **pin**
the member at edit time — it is kept through the regen and the generator cedes
its slot rather than emitting a twin over it. A pinned member no longer follows
the recipe (it holds its position through a param change; re-seat it by hand if
the frame moves around it). The freeze (Chapter 6) remains as an optional lock
for a settled design.

**Save / Save As / Load** manage named `.framespec` templates — your barn
starter, your saltbox. Loading one replaces the palette's spec; Draw makes it
real.

`TRoughIn` is the command-line alternate: it emits a single bent of the type
set in the palette's rough-in setting, prompting for a base point. The tree
editor is the primary path.

---

*Next: [Chapter 6 — The Break: `TFreeze`](06-freeze.md).*
