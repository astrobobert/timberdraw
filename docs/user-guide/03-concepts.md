# 3. Concepts: How TimberDraw Thinks

This is the one chapter to read before touching anything. Five ideas explain every
command in the app. Terms in **bold** are defined in the [Glossary](../../GLOSSARY.md).

---

## 3.1 The timber is the truth

![Figure 3-1](images/Figure%203-1.png)

> **Figure 3-1 — A managed timber carries everything about itself.**

<!-- capture: one girt from the quick-start frame, isolated, with callout boxes
(blue) pointing at the solid: "geometry", "joinery", "labels", "connections" —
arrows from a single box labeled "all stored ON the timber". -->

Every stick TimberDraw draws is a **managed timber**: a 3D solid that carries its
own geometry, joinery, and identity with it, inside the drawing. There is no
separate database or side file — copy the drawing and everything travels along.

Two rules follow:

- **Edit with the verbs, not the grips.** Dragging a grip changes the solid but
  not what the timber knows about itself. Every change goes through a command
  (`TSection` to resize, `TFit` to trim, `TJoint` to cut joinery, ...), which
  updates both together.
- **What you see is what gets built.** The cut list, shop drawings, and laser
  scribe files are all *read* from the timbers. They never recalculate geometry,
  so they cannot disagree with the model.

## 3.2 Recipe, skeleton, freeze

![Figure 3-2](images/Figure%203-2.png)

> **Figure 3-2 — The one-way pipeline.**
<!-- capture or diagram: three panels left to right — the TDraw tree editor
("recipe"), the emitted frame ("skeleton"), a padlock over the Generate Frame
button ("frozen"). Arrow between each; the last arrow labeled "TFreeze — one way". -->

You do not model a frame stick by stick. You edit a **recipe** — the frame spec in
the `TDraw` tree editor (bents, walls, bays, member sizes) — and the generator
emits the whole **skeleton** of managed timbers each time you press **Generate
Frame**.

While the recipe is live, re-generating *replaces* the skeleton. That is the point:
change the span, Generate, get a new frame. But it also means hand edits to a
pre-freeze skeleton are thrown away on the next Generate.

When the skeleton is right, you **freeze** (`TFreeze`, or the Freeze button on the
palette). The freeze is one-way: the generator locks, Generate refuses, and
from then on the timbers themselves are the only truth. After the break there is
no difference between a timber the generator made and one you placed by hand —
every verb works the same on both.

**Freeze as soon as the skeleton is right, and before any hand-cut joinery or
infill.**

## 3.3 The grid is an address, not a driver

![Figure 3-3](images/Figure%203-3.png)

> **Figure 3-3 — Grid bubbles under the quick-start frame.**

<!-- capture: plan view of the quick-start frame with the structural grid visible;
annotate one numbered bubble ("bent line") and one lettered bubble ("wall
line") in blue. -->

The **structural grid** — numbered **bent** lines one way, lettered **wall** lines
the other (letters skip I and O) — exists to locate and label timbers, nothing
more. Moving a timber never moves a grid line, and a grid line never moves a
timber. The grid sits flat under the frame and is redrawn on demand (`TGrid`).

A timber's address is *derived* from where it sits: the post at bent 2, wall A is
`P-2A`. Free timbers you place by hand get addresses by being assigned to a group
(`TAssign`), not by touching a line.

## 3.4 Three IDs per timber

![Figure 3-4](images/Figure%203-4.png)

> **Figure 3-4 — One timber, three names.**

<!-- capture: single brace with three callouts (blue): its location label, its
cut-mark, its production number. Yellow highlight on a second identical brace
labeled "same cut-mark". -->

Every timber carries three identifiers, each doing a different job:

| ID | Question it answers | Behavior |
|---|---|---|
| **Location label** | *Where does this stick install?* | Unique per timber (e.g. `P-2A`, `EG-B-I`, `J-A-1`) |
| **Cut-mark** | *What do I cut / buy?* | Shared by identical sticks — the buy list groups by it |
| **Production number** | *Which physical stick is this?* | Stable serial that survives every edit and re-cut |

Repetitive families (braces, joists, commons, purlins) lean on the cut-mark:
twelve identical braces are one line on the buy list and one scribe drawing set,
with a count.

## 3.5 Reference faces: RS1–RS4 and the ends

![Figure 3-5](images/Figure%203-5.png)

> **Figure 3-5 — Face numbering on a squared timber.**
<!-- capture or diagram: axonometric of a plain timber; label the four sides
RS1–RS4 and the two ends 5 and 6; highlight the reference face and the
reference arris in yellow. -->

Layout on real wood needs a datum. Each timber has 6 faces: 4 sides, numbered
**RS1–RS4**, and 2 ends, **5** and **6**. RS1 is the **reference face**; the edge
it shares with RS2 is the **reference arris** — the datum every scribe drawing
and layout measurement hangs from. In the shop you square the stock, pick and
mark the reference face first, and everything else measures from there.

## 3.6 The model stands up in world space

![Figure 3-6](images/Figure%203-6.png)

> **Figure 3-6 — Z is always up.**

<!-- capture: quick-start frame in SE isometric with the WCS icon visible;
annotate "frame stands Z-up in world coordinates" and "UCS only chooses where
it lands". -->

The frame always stands upright in AutoCAD's world coordinate system — Z is up,
the grid lies on the floor. Your current **UCS** only decides where and at what
rotation a frame or timber is *placed*; it never tilts the model. The UCS preset
commands (`TUcsPlan`, `TUcsBent`, `TUcsWall`) exist purely to make placement
comfortable.

---

*Next: [Chapter 4 — Quick Start](04-quick-start.md), where these five ideas
happen in fifteen minutes.*
