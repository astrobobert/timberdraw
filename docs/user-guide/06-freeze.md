# 6. The Break: `TFreeze`

The freeze is the moment the recipe stops mattering and the timbers take over.
It is one command, it is one-way — and since joinery replay and member pinning
(Chapters 5.4 and 3.2) made regeneration safe for hand work, it is **optional**:
a lock for a settled design, not a step every frame needs.

---

## 6.1 What freezing does

![Figure 6-1](images/Figure%206-1.png)

> **Figure 6-1 — Before and after the gate.**

<!-- capture: two shots of the Frame tab's action bar — Generate Frame enabled
with "Freeze", then refused with the button reading "Frozen"; blue callout on
the changed button. -->

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
runs again *replaces the skeleton*, taking your re-sectioned girt and the
joinery cut into skeleton members with it. The freeze exists to make that
impossible, not merely discouraged. One direction, no surprises.

If you genuinely need to go parametric again, start a fresh frame (a new frame
tag) and rebuild — deliberately, not by accident.

## 6.3 When to freeze

Rarely — regeneration is safe for hand work now. Hand-placed timbers are never
erased; **joinery replays** onto the fresh skeleton automatically (Chapter 5.4);
and a shape-edited skeleton member (`TProfile` / `TFit` / `TSection` / `TScarf`)
is **pinned** at edit time — it survives the regen and the generator cedes its
slot. What a regen still costs you: pinned members stop following the recipe
(a param change moves the skeleton around them — re-seat with `TFit` or
`TJoin > Modify` if they drift), and anything the replay reports as unmatched
needs a `TJointSync`.

So freeze when the *design* is done and you want the recipe locked — a
deliberate end-of-design gesture, not a prerequisite for hand work. The
warning signs it exists for are gone; what remains is certainty.

---

*Next: [Chapter 7 — The Structural Grid and Labels](07-grid-labels.md).*
