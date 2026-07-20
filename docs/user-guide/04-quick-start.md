# 4. Quick Start: A Frame in Fifteen Minutes

The whole pipeline once, on the smallest useful frame: **two king-post bents,
16' span, 12' eave height, 8:12 pitch, one bay**. No options explained — each
step points at the chapter that does. This same frame appears in every figure in
this guide.

Prerequisite: TimberDraw loaded (`TVer` should answer — see Chapter 2), a blank
drawing, World UCS.

---

## Step 1 — Design: `TDraw`

Type `TDraw`. The TimberDraw palette opens on its **Frame** tab with an empty
tree — a frame starts deliberately, not from a canned seed. Build ours:

1. Click **New**.
2. Right-click the frame node and **Add Bent**; set its **Bent Type** to
   KingPost (the wall lines appear with it).
3. Right-click that bent and **Insert Bent After** — the second bent, and with
   it the bay between them.

Then select the frame node at the top of the tree and set, in the properties
pane:

- **Span**: 16'
- **Eave Height**: 12'
- **Pitch**: 8:12

![Figure 4-1](images/Figure%204-1.png)

> **Figure 4-1 — The frame editor with the quick-start values entered.**

<!-- capture: Frame tab, frame node selected, the three edited values
visible; annotate the three panes (tree / properties / action bar)
in blue. -->

A blank drawing starts with an empty tree; a drawing that already carries a
frame refills the tree with *that* frame when you open or switch to it.
Templates and everything else the editor can do is Chapter 5.

## Step 2 — Generate

Click **Generate Frame**. The frame lands at the current UCS origin: managed
timbers standing Z-up, with the structural grid flat beneath them and a label
on every stick.

Orbit around it. Posts at the corners, tie girts, king posts, rafters, braces —
about two dozen timbers, each one already carrying its address (`P-1A`,
`KP-2`, ...).

![Figure 4-2](images/Figure%204-2.png)

> **Figure 4-2 — The drawn skeleton.**

<!-- capture: SE isometric of the full frame with grid bubbles visible; yellow
highlight on one post, blue callout showing its label. -->

Change a value and Generate again — the skeleton is *replaced* each time, and
that stays your workflow for as long as the design moves: hand-placed timbers
survive a re-Generate, and so does the joinery you cut next (Chapter 5.4).

## Step 3 — Cut the joinery: `TJointAll`

Type `TJointAll` and select the whole frame — window everything; the selected
timbers are the ones that *get* joints. It walks you through the joint recipe
(tenon size, pegs — the defaults are sensible; press Enter through them),
finds every girt-end-to-post contact in the selection, and cuts the whole
batch: tenon on the girt, matching mortise and peg bores in the post.

The command line reports how many joints were cut and how many contacts were
skipped (already jointed — safe to re-run).

![Figure 4-3](images/Figure%204-3.png)

> **Figure 4-3 — A cut joint.**

<!-- capture: close-up of one girt-post connection with the post rendered
semi-transparent or the girt pulled back, showing tenon, mortise, and peg
bores; blue callouts naming each. -->

The full connection catalog — braces, struts, rafter feet, ridge, purlins — is
Chapter 10.

## Step 4 — The cut list: `TBom`

Type `TBom` (or just click the palette's **Output** tab — the BOM loads itself
the first time you look). One row per timber: label, size, length, joinery
tally. Click a row and the matching solid highlights in the model.
**Export CSV** writes the tally out.

![Figure 4-4](images/Figure%204-4.png)

> **Figure 4-4 — The Output tab with one row selected and its timber highlighted.**

<!-- capture: Output tab beside the model; selected row + highlighted solid
both visible. -->

## Step 5 — Shop drawings: `TShop`

Type `TShop`. TimberDraw generates an assembly map for each bent and each wall
(plus floor plans, once the frame has floors) and lays them onto a paper-space
layout named **TM Shop** at 3/8" = 1'-0". Every stick appears as a labeled box
in the context of its neighbors.

![Figure 4-5](images/Figure%204-5.png)

> **Figure 4-5 — The TM Shop layout for the quick-start frame.**

<!-- capture: the TM Shop layout tab showing the bent and wall maps. -->

`TShopClear` removes it all; `TShop` re-runs regenerate cleanly.

## Step 6 — Scribe files: `TScribeAll`

Type `TScribeAll` and pick an output folder (it is cleared first). TimberDraw
writes one `.tsj` file per timber face — the burn paths the TimberScribe laser
head follows: joinery outlines, cut-to-length lines, depth labels, peg marks,
and the timber's label. Identical repetitive sticks (the braces here) collapse
to one drawing set carrying a count.

![Figure 4-6](images/Figure%204-6.png)

> **Figure 4-6 — A scribed timber face.**

<!-- capture: preview of one girt face's .tsj content (or the TimberScribe web
preview): profile linework, cut-to-length lines at both ends, the label. -->

Upload the files to the Pi and burn — Chapter 14.

---

## What just happened

Design (recipe) -> Generate (skeleton) -> joinery -> cut list -> shop drawings
-> laser files. Every downstream product was read straight off the timbers —
nothing was re-entered, nothing can drift.

Notice there was no freeze step: regeneration is safe for your hand work —
joinery replays onto a fresh skeleton, and a skeleton member you shape by hand
is pinned (Chapter 5.4). `TFreeze` remains as an optional end-of-design lock
(Chapter 6).

*Deeper on each step: Chapters 5 (frame editor), 6 (the optional freeze),
10 (joinery), 11–13 (output).*
