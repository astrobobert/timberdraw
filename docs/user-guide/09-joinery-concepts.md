# 9. Joinery Concepts

Before cutting joints (Chapter 10), five minutes on how TimberDraw organizes
them. This chapter is a digest — the full vocabulary lives in the
[Glossary](../../GLOSSARY.md), sections B–E.

---

## 9.1 The anatomy of a cut

![Figure 9-1](images/Figure%209-1.png)

> **Figure 9-1 — Exploded mortise & tenon, every part named.**
<!-- capture or diagram: girt tenon pulled out of a post mortise; blue callouts:
cheek, shoulder, relish, housing, mortise, peg bores; yellow shading on the
bearing surfaces. -->

The words the app uses everywhere, worth fixing precisely:

- **Cheek** — a tenon's broad face (and the matching mortise face).
- **Shoulder** — the step from the timber face down to the tenon; it beds
  against the host and carries the load.
- **Housing** — a shallow recess in the host taking the member's full section,
  for bearing.
- **Seat** — the bearing surface a member rests on or in.
- **Heel** — a birdsmouth's plumb cut; resists down-slope thrust.
- **Relish** — the wood left *beyond the peghole* on a tenon. (Trade fact: the
  relish is not the shoulder inset — TimberDraw uses "shoulder" for that.)

## 9.2 Connections converge: topology + elements, not member names

![Figure 9-2](images/Figure%209-2.png)

> **Figure 9-2 — One engine, three homes.**

<!-- capture: three close-ups side by side — a brace into a post, a strut into a
rafter underside, a strut into a king post — annotated "same joint, same
parameters" in blue. -->

TimberDraw does not have a "brace joint" and a separate "strut joint." A
connection is classified by **how the timbers meet** (an end into a side, an
end onto an end, a crossing bearing) plus the **elements** stacked on that
contact (tenon, housing, shoulder, pegs). The geometry decides which timber is
male and which is female — member roles are just labels.

That's why the commands feel identical across very different-looking joints: a
brace into a post and a strut into a rafter underside are the *same engine* at
different angles, and every parameter you learn once applies everywhere it
appears.

## 9.3 The canonical parameters

Every joint prompt and the Joints pane use the same small parameter set. Each
one, with its zero meaning:

![Figure 9-3](images/Figure%209-3.png)

> **Figure 9-3 — Seat.**
> *[diagram: post section with a girt-end housing; dimension arrow from post
> face to housing floor labeled "Seat (let-in depth)".]*

- **Seat** — how far a housing / gain / shoulder recesses *into the host*.

![Figure 9-4](images/Figure%209-4.png)

> **Figure 9-4 — ShoulderTop / ShoulderBottom.**
> *[diagram: girt end elevation; arrows from top face down to tenon top
> ("ShoulderTop") and bottom face up to tenon bottom ("ShoulderBottom");
> note "0 = flush".]*

- **ShoulderTop / ShoulderBottom** — the inset from the top / bottom face down
  to the tenon, measured *from the face*. **0 = flush** (no step on that side).

![Figure 9-5](images/Figure%209-5.png)

> **Figure 9-5 — Thickness and Offset.**
> *[diagram: girt end section, three variants side by side: centered tenon
> (Offset 0), shifted tenon, barefaced tenon (pushed flush to a cheek).]*

- **Thickness** — the tenon width, entered directly (absolute).
- **Offset** — lateral shift of the tenon across the width. **0 = centered**;
  pushed all the way to a face = **barefaced** (the `TBrace` default).

- **Length** — the tenon's projection into the mortise.

![Figure 9-6](images/Figure%209-6.png)

> **Figure 9-6 — The peg layout.**
> *[diagram: post face with two peg bores; dimensions for Setback and Spacing;
> one bore drawn partial-depth labeled "blind".]*

- **Pegs** — Count, Diameter, Setback, Spacing, and full vs **blind** bore
  (a blind bore stops inside the timber; BlindFlip picks the entry face).

## 9.4 Pegs bore the receiving side only

TimberDraw bores peg holes in the **host** (the mortised timber) only — never
through the tenon. That is deliberate: the framer bores the tenon by hand in
the field, slightly offset toward the shoulder, so driving the peg pulls the
joint tight — **draw-boring**. The app lays out; the draw-bore stays craft.

---

*Next: [Chapter 10 — Cutting Joints](10-cutting-joints.md).*
