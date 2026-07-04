# 4. Quick Start: A Frame in Fifteen Minutes

The whole pipeline once, on the smallest useful frame: **two king-post bents,
16' span, 12' eave height, 8:12 pitch, one bay**. No options explained — each
step points at the chapter that does. This same frame appears in every figure in
this guide.

Prerequisite: TimberDraw loaded (`TVer` should answer — see Chapter 2), a blank
drawing, World UCS.

---

## Step 1 — Design: `TDraw`

Type `TDraw`. The frame editor palette opens, already seeded with a fresh
two-bent king-post frame — exactly what we want.

Select the frame node at the top of the tree and set, in the properties pane:

- **Span**: 16'
- **Eave Height**: 12'
- **Pitch**: 8:12

> **Figure 4-1 — The frame editor with the quick-start values entered.**
> *[capture: TDraw palette, frame node selected, the three edited values
> visible; annotate the four panes (tree / properties / description / buttons)
> in blue.]*

The palette starts from this fresh seed every time it opens — saving named
templates and everything else the editor can do is Chapter 5.

## Step 2 — Draw

Click **Draw**. The frame lands at the current UCS origin: managed timbers
standing Z-up, with the structural grid flat beneath them and a label on every
stick.

Orbit around it. Posts at the corners, tie girts, king posts, rafters, braces —
about two dozen timbers, each one already carrying its address (`P-1A`,
`KP-2`, ...).

> **Figure 4-2 — The drawn skeleton.**
> *[capture: SE isometric of the full frame with grid bubbles visible; yellow
> highlight on one post, blue callout showing its label.]*

While the palette is live you can change a value and click Draw again — the
skeleton is *replaced* each time. That stops being true after the next step.

## Step 3 — Freeze: `TFreeze`

Click **Freeze** on the palette (or run `TFreeze`; frame tag `A` is the
default). The generator locks — Draw now refuses — and from here on the timbers
are edited directly, never regenerated. This is one-way (Chapter 6 explains why
that's a feature).

## Step 4 — Cut the joinery: `TJointAll`

Type `TJointAll`. First it walks you through the joint recipe (tenon size, pegs
— the defaults are sensible; press Enter through them). Then it finds every
girt-end-to-post contact in the frame and cuts the whole batch: tenon on the
girt, matching mortise and peg bores in the post.

The command line reports how many joints were cut and how many contacts were
skipped (already jointed — safe to re-run).

> **Figure 4-3 — A cut joint.**
> *[capture: close-up of one girt-post connection with the post rendered
> semi-transparent or the girt pulled back, showing tenon, mortise, and peg
> bores; blue callouts naming each.]*

The full connection catalog — braces, struts, rafter feet, ridge, purlins — is
Chapter 10.

## Step 5 — The cut list: `TBom`

Type `TBom`. A grid palette opens with one row per timber: label, size, length,
joinery tally. Click a row and the matching solid highlights in the model.
**Export** writes the tally to CSV.

> **Figure 4-4 — The BOM grid with one row selected and its timber highlighted.**
> *[capture: BOM palette beside the model; selected row + highlighted solid
> both visible.]*

## Step 6 — Shop drawings: `TShop`

Type `TShop`. TimberDraw generates an assembly map for each bent and each wall
(plus floor plans, once the frame has floors) and lays them onto a paper-space
layout named **TM Shop** at 3/8" = 1'-0". Every stick appears as a labeled box
in the context of its neighbors.

> **Figure 4-5 — The TM Shop layout for the quick-start frame.**
> *[capture: the TM Shop layout tab showing the bent and wall maps.]*

`TShopClear` removes it all; `TShop` re-runs regenerate cleanly.

## Step 7 — Scribe files: `TScribeAll`

Type `TScribeAll` and pick an output folder (it is cleared first). TimberDraw
writes one `.tsj` file per timber face — the burn paths the TimberScribe laser
head follows: joinery outlines, cut-to-length lines, depth labels, peg marks,
and the timber's label. Identical repetitive sticks (the braces here) collapse
to one drawing set carrying a count.

> **Figure 4-6 — A scribed timber face.**
> *[capture: preview of one girt face's .tsj content (or the TimberScribe web
> preview): profile linework, cut-to-length lines at both ends, the label.]*

Upload the files to the Pi and burn — Chapter 14.

---

## What just happened

Design (recipe) -> Draw (skeleton) -> Freeze (the break) -> joinery -> cut list
-> shop drawings -> laser files. Every downstream product was read straight off
the timbers — nothing was re-entered, nothing can drift.

*Deeper on each step: Chapters 5 (frame editor), 6 (freeze), 10 (joinery),
11–13 (output).*
