# Timber & Joinery Glossary

The shared vocabulary for this app: timber anatomy, joint types, and the parameter names used by the
joinery tools. Grounded in trade usage (Timber Framers Guild, Carolina Timberworks, Sobon / Chappell).
This doc is the single source of truth for naming and seeds the end-user guide.

> **Settled:** reference faces use the scribe **RS1–RS4 (sides) + 5–6 (ends)** numbering; the depth axis uses
> **Top / Bottom shoulders** (face-insets) and the width axis stays **Thickness + Offset** (absolute width +
> lateral shift), so tenon thickness is entered directly.

---

## A. Timber anatomy

A **timber** is a member with a **length** and a cross-section of **width × depth**.

| Term | Meaning | Code |
|---|---|---|
| **Length** | the long axis | `f.Z` / `L` |
| **Width** | horizontal cross-section dimension, in place | `f.X` / `W` |
| **Depth** (= **Thickness**) | vertical cross-section dimension | `f.Y` / `D` |

Every timber has **6 faces = 2 ends + 4 sides**.

| Term | Meaning |
|---|---|
| **Reference face** | the side chosen as the layout datum (top of a beam, outside of a post) |
| **Arris** | a sharp edge where two faces meet; a timber has several (the four long arrises run its length) |
| **Reference arris** | the long arris shared by the two reference faces (the square-rule layout datum) |
| **RS1–RS4 / 5–6** | the scribe numbering: the four sides (RS1–4) and the two ends (5–6) |

Everyday orientation of a beam in place: **top**, **bottom**, and two **sides**.

---

## B. Joint types (by how the two timbers meet)

**End → side** (one timber's end dies into another's face):
- **Mortise & tenon** — a tenon (reduced end) seats in a mortise (cavity).
- **Housing** — a shallow recess taking a member's full section, for bearing.
- **Gain** — a shallow housing (loosely, any shallow let-in).
- **Shoulder** — a bearing notch the member seats against (rafter foot/head, girt shoulder).
- **Dovetail** — a flared (wedge-shaped) end + matching socket that resists withdrawal; a **dovetailed
  housing** drops a member in and locks it from pulling back out (purlin → rafter).

**End → end:**
- **Scarf** — a lengthwise splice. Parts: *blade, abutment, table, key, undersquint*.

**Bearing / lap / cross:**
- **Lap** — crossing members, each halved.
- **Birdsmouth** — a notch where a rafter crosses a plate: a **seat** (level cut, bears on the plate **top**) +
  a **heel** (plumb cut). The seat carries the load; the heel's position shifts across the plate with the
  plate's elevation and pitch, and it bears against a plate **side** face (resisting down-slope thrust) only
  when it lands on one.

**Pinning:**
- **Peg / trunnel** — a riven or turned dowel (~¾–1″).
- **Draw-bore** — offsetting the peg holes slightly so driving the peg pulls the joint tight. Not produced by
  the app — do this **by hand in the field at installation** (recommended practice).

---

## C. Joint parts (anatomy of a cut)

| Term | Meaning |
|---|---|
| **Cheek** | a tenon's broad face (and the matching mortise face) |
| **Shoulder** | the face on the tenoned member that beds against the host — the step from the timber face down to the tenon |
| **Relish** | the wood left **beyond the peghole** on a tenon (or beyond the mortise to the timber end); load-resisting |
| **Haunch** | a short, shallow stub on the edge portion of a tenon (projects less than the main tenon, beds in a matching shallow groove). Lets a tenon sit near a timber's end without breaking the mortise out through the end — the mortise stays **closed** — while holding the faces flush and resisting twist |
| **Seat** | the bearing surface a member rests on or in (e.g. a birdsmouth's level cut on the plate top) |
| **Heel** | a birdsmouth's plumb (vertical) cut; resists down-slope thrust only when it bears against a side face |

---

## D. Frame connections (named joints)

Each connection is a contact **topology** + composable **elements** — the member roles are just labels (the
geometry decides which timber is male/female). Connections therefore **converge**: in the code each
topology + element is **one reusable cutter** (an *engine*), and a new role pair reuses an engine rather than
adding joint code. The set of engines is small; the catalog of connections below all map onto them.
*(catalog current 2026-06-25)*

**Engines (the cutters):**

| Engine | What it cuts | Command(s) |
|---|---|---|
| **Tenon onto a face** | a strut / brace / V-strut **end** bearing flush on any flat host face at any angle; one wall square to the face, one along the member axis; world-up-keyed shoulders; barefaced via Offset | `TStrut`, `TBrace` (`StrutTenonJoint`) |
| **Box tenon** | an orthogonal girt **end** into a post: tenon + optional housing + pegs | `TJoint` (`GirtPostJoint`) |
| **Sloped wedge** | a rafter **foot** let into a post side: level seat + pitched top housing + tenon | `TRafterFoot` |
| **Housing / gain** | a let-in recess that takes a member's end for bearing | `TCommonRidge`, `TRidge` |
| **Birdsmouth** | a crossing seat + heel | `TCommonEave` |
| **Dovetail housing** | a flared drop-in that locks against withdrawal | `TPurlin` |
| **Shoulder notch** | a right-triangle bearing seat | `TRafterHead` |
| **Scarf** | a lengthwise end-to-end splice | `TScarf` |

**Connections → engine:**

| Connection | Contact | Element / engine | Command | Notes |
|---|---|---|---|---|
| **Girt → Post** | end → side | box tenon (+ housing, pegs) | `TJoint` | the square workhorse joint; housing seats the girt end, pegs pin the tenon |
| **Strut / V-strut → Rafter** | end → side | tenon onto a face | `TStrut` | strut head beds on the rafter **underside**; tongue rises into a mortise (handles any strut angle) |
| **Strut → King-post / Post side** | end → side | tenon onto a face | `TStrut` | the strut **foot** into a vertical post side — same engine, different host face |
| **Brace → Girt / Post** | end → side | tenon onto a face | `TBrace` | thinner (1.5″) and **barefaced** (tongue flush to a cheek) by default; beds on a level girt **underside** or a post side |
| **Rafter foot → Post** | end → side | sloped wedge | `TRafterFoot` | full-section seat (level + pitched top) recessed into the post side, plus a reduced tongue |
| **Rafter head → King post** | end → side | shoulder notch | `TRafterHead` | right-triangle bearing seat at the rafter underside, into the king-post side |
| **Common rafter → Ridge** | end → side | housing | `TCommonRidge` | the rafter **head** is let into a recess in the ridge (the ridge is the host) |
| **Ridge → King post** | end → side | housing (drop-in) | `TRidge` | the ridge drops into a chamfered pocket risen to the apex on the king-post top |
| **Common rafter → Eave girt** | bearing / crossing | birdsmouth | `TCommonEave` | beds on the girt **top** (seat) + against a **side** (heel); the rafter runs on past as the eave **tail** |
| **Purlin → Rafter** | end → side | dovetailed housing | `TPurlin` | the purlin **end** is a dovetail dropped into a flared housing in the rafter's back; the flare resists pull-out |
| **Beam → Beam** | end → end | scarf | `TScarf` | a lengthwise splice |

---

## E. Parameters (canonical names)

| Parameter | Meaning |
|---|---|
| **Seat** | the let-in depth — how far a housing / gain / shoulder recesses **into the host**. |
| **ShoulderTop** / **ShoulderBottom** | the inset from the top / bottom face that forms the bearing step (the depth axis); measured **from the face**; **0 = flush**. |
| **ShoulderSide1** / **ShoulderSide2** | a **housing's** inset from each side face that forms a bearing step (the width axis); measured **from the face**; **0 = flush**. A housing's footprint is the four shoulders (Top/Bottom + Side1/Side2); a **tenon** instead uses absolute **Thickness + Offset**. |
| **Thickness** | the tenon width (absolute; **0 = full** section width). *(Tenons only — housings use side shoulders.)* |
| **Offset** | lateral shift of the tenon across the width (**0 = centered**; pushed to a face = barefaced). *(Tenons only.)* |
| **Length** | a tenon's projection into the mortise (or a dovetail tongue's projection past its housing). |
| **Heel** | a birdsmouth's heel let-in — how far the plumb heel cut beds **inside the heel face** (resists down-slope thrust). Pairs with **Seat** (the level seat let-in below the girt top). |
| **Dovetail**: Seat, Length, Width, Depth, Angle | the dovetail layout: **Seat** = housing recess depth, **Length** = tongue projection, **Width** = tongue width, **Depth** = housing band depth into the host's back, **Angle** = the flare (taper) angle that resists pull-out. |
| **Relish** *(future)* | tenon material beyond the peg; not a parameter yet (`Setback` + `Length` govern it today). |
| **Peg**: Count, Diameter, Setback, Spacing, Bore (full/blind), BlindDepth, BlindFlip | the peg layout. |

*Note:* the **depth axis** uses Top / Bottom shoulders (face-insets); the **width axis** uses **Thickness +
Offset** (absolute), so tenon thickness is entered directly, not derived.

---

## F. Parameter rename map (applied)

The joinery code used several names for the same idea. The pass folded them to the canonical names above
(DONE 2026-06-23; geometry-neutral, builds + 2 arch tests pass).

| Concept | Old names (across specs) | New |
|---|---|---|
| Let-in depth | `Cut` (Housing, Shoulder), `Depth` (RafterFoot), `SitDepth` (RafterHead), `Seat` (Ridge) | **`Seat`** |
| Depth-axis insets | `RelishTop` + `RelishBot` | **`ShoulderTop` / `ShoulderBottom`** |
| Tenon width | `Thickness` + `Offset` | **kept** (absolute width + lateral offset, unchanged) |
| Tenon projection | `Length` | **`Length`** (keep) |

**Specs/code touched:** `TenonSpec`, `HousingSpec`, `ShoulderSpec`, `RafterFootSpec`, `RafterHeadSpec`,
`RidgeHousingSpec`; their `Review*` menus; and the cutter bodies (`SectionBox`, `EmitTenon` / `EmitHousing` /
`EmitShoulder`, the rafter/ridge cutters). `RidgeHousingSpec.Seat` and `PegSpec` were already fine. The width
axis (`Thickness` + `Offset`) was kept per decision — only the depth insets and the let-in names changed;
defaults preserved, so joints cut identically.
