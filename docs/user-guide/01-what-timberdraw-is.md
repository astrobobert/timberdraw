# 1. What TimberDraw Is

TimberDraw is the design side of the **Timber Frame Suite** — an open-source
CAD/CAM system for traditional timber framers. The suite helps you design
timber frames, produce shop drawings and cut lists, and laser-scribe accurate
joinery layouts directly onto the timbers. The joinery is still cut by hand
using traditional tools — the laser replaces tape-measure layout, not
craftsmanship.

---

## 1.1 The suite in one picture

![Figure 1-1](images/Figure%201-1.png)

> **Figure 1-1 — The pipeline.**
> *[diagram: three stages left to right — TimberDraw in AutoCAD (frame model
> on screen) -> a `.tsj` file icon -> the TimberScribe print head on a timber
> (or the burned-timber photo). Label the handoff ".tsj — one file per timber
> face".]*

1. **TimberDraw** (this guide) — an AutoCAD plugin. Design the frame, cut the
   joinery in the model, get the labels, cut list, and shop drawings.
2. **`.tsj` files** — one burn-path file per timber face, exported straight
   from the model.
3. **TimberScribe** — a self-propelled laser print head driven by a Raspberry
   Pi. It rides the timber and burns the layout: joinery outlines, cut lines,
   bore centers, labels. You cut to the lines with the tools you already use.

## 1.2 What you get out

- A **3D frame model** where every timber knows its own geometry, joinery,
  and identity (Chapter 3).
- A **cut list / BOM** with stock lengths and board feet (Chapter 11).
- **Shop drawings** — assembly maps per bent, wall, and floor (Chapter 12).
- **Laser scribe jobs** for every face of every stick (Chapter 13).

## 1.3 What it is not (yet)

- **Not an engineering tool** — no load calcs, no stamped output. Size your
  timbers with your engineer.
- **Not CNC** — the output is layout marks for hand cutting, by design.
- **Not a general CAD trainer** — you need basic AutoCAD: open a drawing,
  pick points, orbit, plot. Nothing deeper is assumed.

## 1.4 How to read this guide

Chapter 3 (Concepts) is the one required read — five ideas that explain every
command. Chapter 4 runs the whole pipeline in fifteen minutes on a small
frame; the rest of the guide deepens each step in workflow order. Trade terms
are **bold** on first use and defined in the [Glossary](../../GLOSSARY.md).

---

*Next: [Chapter 2 — Installation](02-installation.md).*
