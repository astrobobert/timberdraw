# 7. The Structural Grid and Labels

Remember the rule from Chapter 3.3: the grid is an **address**, never a driver.
This chapter is how the addressing actually reads — on the grid, and in the
label every timber wears.

---

## 7.1 The grid (`TGrid`)

> **Figure 7-1 — The grid under the quick-start frame.**
> *[capture: plan or shallow iso; grid bubbles visible; blue callouts on a
> numbered bent bubble and a lettered wall bubble; note the yellow ground
> columns vs blue.]*

`TGrid` redraws the structural grid for the current frame, flat on the floor
under it. The grid is **derived from the drawing** — it scans the frame's
managed timbers, so assigning a new sub-post re-letters and re-numbers on the
next `TGrid`. Only timbers that meet the floor draw a line.

Conventions:

- **Numbers are bent lines**, in frame order. A sub-bent between bents 1 and 2
  takes an intermediate number: **1.1**.
- **Letters are wall lines**, skipping **I** and **O** (never mistakable for
  1 and 0). Intermediate wall lines read **A.1**.
- Column bubbles for **ground columns are yellow; everything else blue**.

## 7.2 Reading a label

> **Figure 7-2 — A labeled frame.**
> *[capture: SE isometric of the quick-start frame with several labels
> legible; blue callouts decoding one bent-plane label and one wall-plane
> label.]*

Every label follows one grammar: **family code first, then the anchor, then
qualifiers** — so sorted lists group by member family.

`FAM-ANCHOR-quals`

- **The family code** says what it is: `P` post, `KP` king post, `QP` queen
  post, `RF` rafter, `ST` strut, `VS` V-strut, `HB` hammer beam, `HP` hammer
  post, `TG` tie girt, `FG` floor girt, `EG` eave girt, `SL` sill, `SB`
  summer, `J` joist ...
- **The anchor** says where, and its *shape* tells you the plane:
  - **Digit-first = bent plane**: `P-2A` is the post at bent 2, wall A;
    `TG-2` the tie girt of bent 2.
  - **Letter-first = wall plane**: `EG-B-I` is the eave girt on wall B in
    bay I; a member spanning bays reads a range, `EG-B-I-II`.
  - Free timbers are **owner-addressed**: `J-A-1` is joist 1 of assignment
    group A — located by dimension, not by a grid line.
- **Qualifiers** disambiguate: hand `L`/`R`, level `Dn`/`Up` (a floor girt
  below a tie girt on the same post line), and a per-anchor sequence number.

Two label economies to know:

- **Braces wear a group symbol** (`*`, `**`, ...) shared by every brace of the
  same size and shape — twelve identical braces are one symbol, matching how
  they're cut and carried.
- **Commons and purlins number per bay** (`C1...`, `P1...` restarting each
  bay), owned by the wall their eave girt sits on.

**Brace groups.** Two braces share a symbol when three things match: the
**cross-section** (to the quarter inch), the **angle** from horizontal (to the
degree), and the **overall length of the finished stick** (to the inch).
Overall length is measured on the cut solid, *projecting tenons included* — so
two braces with the same section and the same leg runs can still land in
**different groups when their tenons differ**: a longer tenon makes a longer
stick, and the group answers *what do I cut*, not just where the brace spans.
Same reasoning in reverse: a bent brace and a wall brace that finish identical
collapse to one symbol however they were placed. Symbols are assigned in a
stable order (section, then angle, then length), and they're re-derived
**automatically** — at every Draw, and the moment a brace is edited: placed,
copied, re-seated, re-sectioned, refit, or any of its joints cut, cleared,
deleted, or re-synced. `TRelabelBraces` runs the same regroup on demand and
echoes a table (symbol, section, angle, length, count), which is the place to
look when grouping seems wrong — near-identical braces splitting over an inch
of tenon show up right there.

The label is one of the timber's three IDs (Chapter 3.4) — it answers *where
does this install*, while the cut-mark answers *what do I cut*.

## 7.3 Giving free timbers an address (`TAssign`)

A hand-placed timber starts unaddressed. `TAssign` puts it in the frame's
bookkeeping:

1. Select the rows on the **Browser** tab (Chapter 8.4) — the one assign
   surface — or run `TAssign` and pick in the model.
2. Give the target: a **bent** (a free post standing on a grid intersection
   takes the intersection itself, e.g. `2C`), a **wall + bay**, or a **floor
   level + bay**. From the Browser the target comes from its Frame /
   Assign-to fields and **Apply** (the rows name themselves Bent / Wall /
   Floor to match the choice); from the command line, the prompts walk you
   through the same choice. Every assigned timber gets a type-first label
   (`P-2C`, `J-1-1`, `G-B-1`) — family prefix, owner, and a sequence where
   the owner holds several of a kind. A floor member's bay rides as its
   grouping designation (the Browser and shop maps partition on it); the
   label stays `J-<floor>-n`.

Assigned timbers get labels in the same grammar (`J-A-1`), appear in the
grid's derivation, and group correctly in the BOM, shop maps, and scribe
export.

## 7.4 Older frames: `TRelabel`

Frames drawn before a labeling convention existed (Dn/Up levels, type-first
bent labels) can be retrofitted in place: `TRelabel` rewrites the labels to
the current grammar without touching geometry. Appendix D's troubleshooting
table lists the symptoms that call for it.

---

*Next: [Chapter 8 — The Assembly Palette and the Editor Verbs](08-editor-verbs.md).*
