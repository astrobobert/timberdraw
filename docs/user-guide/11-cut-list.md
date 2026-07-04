# 11. The Cut List (`TBom`)

The BOM is a *read*, not a build: every managed timber already knows its label,
size, length, and joinery, so `TBom` just tallies what the timbers carry. If the
model is right, the list is right.

---

## 11.1 The grid

> **Figure 11-1 — The Timber BOM palette for the quick-start frame.**
> *[capture: the Timber BOM palette docked beside the model, quick-start frame
> loaded, a few rows visible; annotate the column groups (identity / stock /
> joinery) in blue.]*

Type `TBom`. The **Timber BOM** palette opens with one row per timber. Click any
column header to sort. The columns, left to right:

| Column | Meaning |
|---|---|
| **Label** | The location label — where the stick installs |
| **Type** | Member family (Post, BentGirt, Brace, Joist, ...) |
| **W**, **D** | Section width x depth, inches |
| **Overall (in)** | Length **measured from the finished solid — including projecting tenons** |
| **Buy (ft)** | Stock length to buy for that overall length |
| **BF** | Board feet (W x D x Buy / 12) |
| **Joints** | How many joints touch this timber |
| **Tenon / Mortise / Housing / Shoulder / Dovetail** | Count of each joint element, per side (a tenon on this stick is a mortise on its mate) |
| **Peg** | Peg bores in this timber (pegs bore the receiving side only) |
| **Untyped** | Joints with geometry but no named connection type |

## 11.2 Finding a stick

Select a row (or several — Ctrl/Shift work) and the matching solids **highlight
in the model**. Sort by Label first if you're chasing one timber; sort by Type +
W + D to see families group together.

> **Figure 11-2 — Two rows selected, two braces lit in the model.**
> *[capture: BOM palette with two brace rows selected and both braces
> highlighted; use the selection highlight as-is, callouts in blue.]*

## 11.3 Reading it right

- **Overall vs Buy.** Overall is tip-to-tip on the finished stick, tenons
  included. Buy (ft) is the stock you order. The difference is your trim
  allowance — don't cut the stock to Overall at the saw; the scribe drawings
  carry the real cut-to-length lines.
- **The buy list groups by sameness.** Identical sticks (same type, section,
  length, joinery) sort together — that's the cut-mark idea from Chapter 3: cut
  once, count many.
- **Untyped isn't an error** — it means a joint was cut without a named
  connection type (older drawings, or hand-cut features). The geometry is still
  counted; only the per-kind columns can't classify it.

## 11.4 Refresh and export

The model doesn't push updates into an open palette — after editing timbers,
click **Refresh** to re-read. **Export** writes the current table to CSV
(column order as shown), ready for a spreadsheet or the sawyer's email.

---

*Next: [Chapter 12 — Shop Drawings](12-shop-drawings.md).*
