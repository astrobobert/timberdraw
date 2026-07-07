# 12. Shop Drawings (`TShop`)

Shop drawings answer the raising-day question: *which stick goes where, next to
what?* `TShop` generates an assembly map for every plane of the frame — each
timber drawn in its real place as a labeled box, in the context of its
neighbors.

---

## 12.1 What you get

> **Figure 12-1 — The map set for the quick-start frame.**
> *[capture: the TM Shop layout tab, whole sheet visible: two bent maps, the
> wall maps, Floor 0; annotate one of each kind in blue.]*

Type `TShop`. One command builds the whole set:

- **One map per bent** — the bent elevation with its posts, girts, king post,
  rafters, braces, plus the longitudinal members that cross it shown as context.
- **One map per wall** — each lettered wall line's elevation.
- **One floor plan per floor level** (once the frame has floors) — joists and
  summers looking down, spaced against the girts that carry them.
- **Floor 0** — the structural grid with the post feet and the frame sills
  drawn in place: the column/foundation plan.

Everything lands on a paper-space layout named **TM Shop** at **3/8" = 1'-0"**,
one viewport per map. The command line lists every map and its member count.

## 12.2 Reading a map

> **Figure 12-2 — One bent map, annotated.**
> *[capture: close crop of one bent map; blue callouts on: a member label, a
> context member with an X mark, a context member with a + mark, the
> view-direction marker.]*

- **Boxes, not cut sticks.** Each timber is its nominal (pre-joinery) outline —
  the map shows assembly, the scribe drawings show the cuts.
- **Labels sit where the stick sits.** Identical repeats that share a label
  (same-symbol braces) are labeled once per distinct string.
- **X and + are direction marks** on members you're seeing end-on (a joist
  housed in a girt, a girt crossing a bent): **X = the stick runs toward you**,
  **+ = it runs away from you** — the arrow-tip / arrow-feathers convention.
- Each drawing carries a **view-direction marker** telling you which way you're
  looking at that plane.

## 12.3 Regenerating and clearing

`TShop` always rebuilds from scratch — prior shop output is cleared first, so
re-running after edits is the normal workflow, never a merge. `TShopClear`
removes all shop geometry and the layout without regenerating.

## 12.4 Printing

Plot the TM Shop layout as usual; 3/8" = 1'-0" reads well on 11x17 for typical
frames. *(Plot-style recommendations to be added after a print test — see
outline.)*

---

*Next: [Chapter 13 — Scribe Export](13-scribe-export.md).*
