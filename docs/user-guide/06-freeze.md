# 6. The Break: `TFreeze`

The freeze is the moment the recipe stops mattering and the timbers take over.
It is one command, it is one-way, and knowing *when* to run it is most of what
this chapter has to say.

---

## 6.1 What freezing does

> **Figure 6-1 — Before and after the gate.**
> *[capture: two shots of the Frame tab's action bar — Generate Frame enabled
> with "Freeze", then refused with the button reading "Frozen"; blue callout on
> the changed button.]*

Run `TFreeze` (default frame tag `A`), or click **Freeze** on the frame editor.
Geometry does not change — the timbers were already managed solids. What flips
is the **gate**:

- The generator locks. **Generate refuses** (`"frame A is frozen -- the
  parametric generator is locked"`), so nothing can ever re-emit the skeleton
  over your hand work.
- The frame's recipe (bent type, seed parameters, placement) stays stored on
  the frame — a record of where the skeleton came from, kept for a future
  re-seed feature.
- From now on every edit goes through the managed verbs (Chapter 8), and the
  skeleton/free distinction is gone: a generator post and a hand-placed post
  are the same kind of thing.

## 6.2 Why one-way

A two-way gate would mean the generator could run again — and a generator that
runs again *replaces the skeleton*, taking your re-sectioned girt, your cut
joinery, and your floor system with it. The freeze exists to make that
impossible, not merely discouraged. One direction, no surprises.

If you genuinely need to go parametric again, start a fresh frame (a new frame
tag) and rebuild — deliberately, not by accident.

## 6.3 When to freeze

**As soon as the skeleton is right, and always before:**

- hand-cut joinery (`TJoint`, `TBrace`, ... — Chapter 10),
- infill and floors (`TPlace`, `TJoist`, ... — Chapter 8),
- any per-timber change you want to keep (`TSection`, `TFit`, `TScarf`).

While the palette can still re-Generate, treat the model as disposable. The
moment you would be upset to lose an edit, you should already have frozen.

---

*Next: [Chapter 7 — The Structural Grid and Labels](07-grid-labels.md).*
