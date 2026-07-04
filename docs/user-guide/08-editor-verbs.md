# 8. The Assembly Palette and the Editor Verbs

After the freeze, this is how you build: place timbers, connect them, fit
them, and give them addresses. Every verb works on any managed timber —
generator-made or hand-placed.

---

## 8.1 The assembly palette (`TPanel`)

> **Figure 8-1 — The assembly palette.**
> *[capture: TPanel; blue callouts on: the section tree with a leaf selected
> (sticky section), the verb buttons, the UCS orientation buttons, the
> Assembly pane target boxes.]*

`TPanel` opens the assembly palette:

- **Sections as a tree** — member types (Post, Girt, ...) with each
  cross-section (W x D) as a leaf. Selecting a leaf makes it the **sticky
  section**: the placement verbs stop asking for dimensions until you pick a
  different leaf.
- **Verb buttons** fire the commands below — same commands, typed or clicked.
- **Orientation buttons** fire the UCS presets.
- **The Assembly pane** holds the current assignment target for `TAssign`
  (Chapter 7.3).

**UCS presets:** `TUcsPlan` / `TUcsBent` / `TUcsWall` orient your working
plane for placement — plan, a bent's cross plane, a wall's long plane. The
frame itself always stands Z-up (Chapter 3.6); the presets just make picking
comfortable.

## 8.2 The verbs

One per job. Each asks you to pick, does one thing, and leaves the timber
knowing about it.

| Verb | You pick | It does |
|---|---|---|
| `TPlace` | a point + extrusion direction | Places one timber of the sticky section. |
| `TSpan` | two **timbers** | Finds their facing faces and fills the gap with a new member — post to post, girt to girt. |
| `TJoin` | two **faces** | Facing faces: a square-ended filler. Angled faces: a mitered knee. |
| `TFit` | a timber's **end**, then a target face | Trims or extends that end onto the face (square or mitered); the other end stays put. |
| `TSection` | a timber | Re-sections it (new W x D) in place. |
| `TScarf` | a timber + a point | Splits it into two pieces with a scarf, and remembers the splice interface. |
| `TJoist` | a bay/wall + spacing | Places a whole row of floor joists — flush tops, optional drop (Chapter 7.3 for how floors are addressed). |

> **Figure 8-2 — TSpan before/after.**
> *[capture: two posts with a gap, then the same view with the spanning girt
> placed; yellow highlight on the new member.]*

> **Figure 8-3 — TFit before/after.**
> *[capture: a girt end short of a post face, then fitted flush to it; blue
> callout on the picked end and target face.]*

## 8.3 Moving, copying, deleting

Plain AutoCAD works: **MOVE, ROTATE, MIRROR, ALIGN** (and rigid grip drags)
carry a managed timber's stored identity along with the solid — no special
move command. Two cautions:

- **Rigid motions only.** Stretching or scaling a managed solid is the one
  thing that desynchronizes it — resize with `TSection`, refit with `TFit`.
- **ERASE deletes cleanly** — everything the timber knew lived on the timber.
  Run `TScan` afterward if you care about the node markers.

## 8.4 Seeing what you have: `TScan` and `TBrowse`

- **`TScan`** rescans every managed timber for **face coincidence** and marks
  the derived connection nodes — the model's own report of what touches what.
  Run it after moving or fitting to see the connectivity you actually have.
- **`TBrowse`** opens the frame browser: every timber in a filterable list.
  Selecting rows **highlights** the solids (the view doesn't jump);
  double-click **zooms** to one. Rows can be sent to `TAssign` — the browser
  is the assign-and-review surface for labeling sessions.

> **Figure 8-4 — The frame browser with three joists selected and highlighted.**
> *[capture: TBrowse beside the model; three rows selected, the matching
> joists highlighted.]*

---

*Next: Part 4 — [Chapter 9, Joinery Concepts](09-joinery-concepts.md).*
