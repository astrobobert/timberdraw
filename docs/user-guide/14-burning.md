# 14. TimberScribe: Burning the Timber

The last mile. This chapter is deliberately thin — full setup and operation
live with the [timberscribe repository](https://github.com/astrobobert/timberscribe)
itself; here is just what the framer at the sawhorses does with the files
Chapter 13 produced.

---

## 14.1 Loading jobs

![Figure 14-1](images/Figure%2014-1.png)

> **Figure 14-1 — The job list on a phone.**

<!-- capture: the TimberScribe web UI job list with a few uploaded jobs. -->

The print head runs a small web server. From a phone or laptop on the same
network, open `http://<pi-address>:5000`, and **upload the `.tsj` files** from
your Scribe folder. Each timber's faces appear as a job; the face-select
dialog previews each face's marks before you commit.

## 14.2 Aligning to the datum

Everything in Chapter 13.2 pays off here:

1. Square the stock; choose and pencil-mark the **reference face (RS1)** and
   the **reference arris**.
2. Set the timber reference-face up, print head aligned to the arris. The
   burn origin is the face's **upper-left corner**.
3. Burn, then roll to the next face in RS order. If the datum was right on
   face one, all four faces agree.

![Figure 14-2](images/Figure%2014-2.png)

> **Figure 14-2 — Print head aligned on the arris.**
> *[photo: the print head sitting on a timber, arris alignment visible.]*

## 14.3 Running a burn

Select the face, check the preview, start. The head propels itself down the
timber and burns the layout. Stay with it — laser safety is the same as any
Class-4-adjacent tool: **no eyes in the beam path, no unattended burns,
mind the char and dust.** *(The hardware guide in the timberscribe repo is
authoritative on safety and settings.)*

Then pick up your chisel. The marks are the layout; the joinery is yours.

## 14.4 The file format

`.tsj` is a small JSON format — one file per face, origin upper-left, inches.
The spec (`TSJ_SPEC.md`) lives in the timberscribe repository. Appendix C has
the one-paragraph version.

---

*Appendices: [A. Command Reference](appendix-a-commands.md) ·
[B. Glossary](appendix-b-glossary.md) · [C. The .tsj Format](appendix-c-tsj.md)
· [D. Troubleshooting](appendix-d-troubleshooting.md)*
