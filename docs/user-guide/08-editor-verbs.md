# 8. The Assembly Palette and the Editor Verbs

After the freeze, this is how you build: place timbers, connect them, fit
them, and give them addresses. Every verb works on any managed timber —
generator-made or hand-placed.

---

## 8.1 The Assembly tab (`TPanel`)

> **Figure 8-1 — The Assembly tab.**
> *[capture: Assembly tab; blue callouts on: the section tree with a leaf
> selected (sticky section), the Brace spec checkboxes, and the labeled verb
> groups in the bottom action bar.]*

`TPanel` opens the palette on its **Assembly** tab:

- **Sections as a tree** — member types (Post, Girt, ...) with each
  cross-section (W x D) as a leaf. Selecting a leaf makes it the **sticky
  section**: the placement verbs stop asking for dimensions until you pick a
  different leaf. Add / Update / Remove maintain the catalog beside it.
- **The Brace spec** — Foot / Head / Angle with checkboxes: the two checked
  are the inputs, the third derives (`TJoin` knees use these legs).
- **The bottom action bar** carries every verb in labeled groups: **UCS
  preset** (Plan / Bent / Wall), **Build timbers** (Place / Span / Brace /
  Joist), **Shape timber** (Fit / Scarf / Section), **Position** (Move /
  Rotate), and **Connectivity** (Scan). Same commands, typed or clicked.

Assigning addresses lives on the **Browser** tab (8.4) — one surface for it.

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
| `TJoist` | two **carriers** + two **run-bound faces** + spacing | Places a whole row of **plain** floor joists between the carriers, filling exactly the run you bounded — flush tops, optional drop. Joinery is deliberate: cut the end dovetails later with a selection + `TJointAll` (Chapter 10.2), or opt in at place time via the **Joint** keyword. Chapter 7.3 for how floors are addressed. |
| `TAdopt` | your own 3DSOLIDs | Converts solids you modeled yourself into managed timbers: measures each one's axes and stock size and **replaces it in place** — from then on it assigns, joints, lists, and scribes like any other stick. Box-like bodies only (a solid filling under 90% of its stock — an arch — is left as-is; shape it with `TProfile` instead). |
| `TProfile` | a timber + a **closed curve** | Cuts the drawn profile straight through the timber's width — the **arched-timber** verb. Draw the arch on the timber's elevation (polyline, circle, or spline), and the cut is carried in the timber's own recipe: it survives moves and joint re-cuts, reads in shop maps, and scribes as real edges. **The profile also trims joinery** — a tenon never re-appears inside the arch (removed is removed); the mate's pocket stays sized to the joint, so resize the joint if the arch eats into it. A shape cut like `TScarf` — UNDO restores it. |
| `TCopy` | timbers + base/destination points | Copies as **new sticks** (8.3): shape and joinery kept, joint ids re-minted, grid address and production number cleared. Repeats like COPY. |

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
- **Joinery travels with the timber.** Moving a jointed timber carries its
  cuts along; when it lands, select it and run `TJointSync` (Chapter 10.3) to
  re-cut its joints against its mates at the new contact.
- **Copy with `TCopy`, not COPY.** Plain COPY clones the timber's *identity*
  along with the solid — grid address, production number, joint ids — so the
  copy masquerades as the original. `TCopy` makes real new sticks: shape and
  joinery are kept but every joint id is re-minted (a copied jointed *pair*
  stays jointed to each other), and the address and production number are
  cleared. `TAssign` to address them, `TJointSync` to re-attach their joints
  at the new location.

## 8.4 Seeing what you have: `TScan` and `TBrowse`

- **`TScan`** rescans every managed timber for **face coincidence** and marks
  the derived connection nodes — the model's own report of what touches what.
  Run it after moving or fitting to see the connectivity you actually have.
- **`TBrowse`** opens the **Browser** tab: every timber in a filterable list,
  grouped Frame -> Bent/Wall -> Bay and sorted by type within each group.
  Selecting rows **highlights** the solids (the view doesn't jump);
  double-click **zooms** to one. The stacked fields below the list are both
  the review and the assign surface: a selected row loads its section and
  address, and one **Apply** commits — section fields re-section the timber if
  you edited them, and the address fields (Frame / Kind / Owner / Bay) are
  WYSIWYG: whatever they show is where Apply puts the selection (already-there
  rows are left alone, so a section-only Apply doesn't reassign). **Frame** is
  a drop-down of the frames actually in the drawing (it defaults to the first
  one present); it stays typable — enter a new tag to start the next frame.
  The list re-reads itself when you open the tab, switch drawings, or joinery
  replaces a timber.

> **Figure 8-4 — The frame browser with three joists selected and highlighted.**
> *[capture: TBrowse beside the model; three rows selected, the matching
> joists highlighted.]*

---

*Next: Part 4 — [Chapter 9, Joinery Concepts](09-joinery-concepts.md).*
