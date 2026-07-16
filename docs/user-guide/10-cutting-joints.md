# 10. Cutting Joints

Two ways to cut every joint: the **Joints pane** (visual, one grid for all
connection types) or the **direct commands** (fastest when you know what you
want). Both drive the same engines, and both are safe to re-run — a joint has
an identity, and cutting it again *replaces* it rather than doubling up.

---

## 10.1 The Joints pane

![Figure 10-1](images/Figure%2010-1.png)

> **Figure 10-1 — The Joints pane with a girt-post pair loaded.**

<!-- capture: TPanel Joints tab; the element stack visible with a couple of
params edited; blue callouts: Pick pair button, element enable checkboxes,
a grayed unavailable row. -->

Open `TPanel` and switch to the **Joints** tab.

1. **Pick pair** (`TJoinPick`) — pick the timber being jointed first (the
   strut / brace / rafter), then the host it beds into. The button itself is
   the readout: it reads **Pick pair** when empty and **Pick (Brace, Post)**
   while holding one.
2. A **fresh pair cuts immediately** with the current connection type and
   parameters — so once the recipe is set, you just pick pair after pair.
3. If the pair **already carries a joint**, its saved settings load into the
   pane instead (nothing is clobbered). Tweak and **Apply** (`TJoinApply`) to
   re-cut it.
4. **Clear joint** (`TJoinClear`) removes the held pair's joint outright —
   every element plus its saved settings, both timbers rebuilt plain. The pair
   stays held, so **Apply** right after re-cuts it fresh at the current
   contact: the quick fix for a joint that ended up displaced (no more
   toggling an element off and back on to force a re-cut).
5. The grid lists every element and parameter across all connection types in
   one stable layout; the selected type decides which rows are live and which
   are grayed. Changing a value re-cuts the real joint as you go.

Every parameter row has a tooltip in plain trade language (Chapter 9 / the
Glossary).

## 10.2 The connection catalog

One section per connection. Each command prompts through its parameters
(Enter accepts the sticky defaults), cuts both sides under one joint id, and
has a matching `...Del`.

| Connection | Command | What it cuts |
|---|---|---|
| **Girt -> post** | `TJoint` | The square workhorse: tenon on the girt end, mortise + housing in the post, peg bores. Host-neutral — a summer end into a girt side cuts the same way. |
| **The batch cutter** | `TJointAll` | The deliberate act: **selection-scoped** — the selected timbers are the ones that *get* joints; their hosts are found automatically. Select the timbers first and then run it (a pickfirst set is honored), or let it ask (AutoCAD's **P**revious re-uses the last set). Then up to five passes, each reviewed only when its members are in scope, Escape skipping just that pass: girt ends -> posts (M&T + pegs), post feet -> **sills** (short unpegged stub), **summer** ends -> girts (tusk tenon), **joist** ends -> carriers (housed dovetail), **commons** -> ridge housing at the head + housed birdsmouth over the eave girt (two reviews, one per cut). Already-cut contacts always skip — safe to re-run. |
| **Brace -> girt / post** | `TBrace` | 1 1/2" barefaced tenon by default (Flip picks the cheek); beds on a girt underside or post side. |
| **Strut / V-strut -> rafter, post, king post** | `TStrut` | The any-angle tenon: strut head into a rafter underside, strut foot into a post side — same engine. |
| **Rafter foot -> post** | `TRafterFoot` | The sloped wedge: level seat + pitched top housed into the post side, plus a reduced tongue. |
| **Rafter head -> king post** | `TRafterHead` | Right-triangle shoulder notch bearing on the king-post side. |
| **Ridge -> king post** | `TRidge` | Drop-in housing: a chamfered pocket at the king-post apex; the ridge lowers straight in. |
| **Ridge -> principal rafter** | `TRidgeRafter` | The same drop-in geometry, housed into a rafter head instead — for bents with no king post, where the two rafters carry the ridge. Run it once per rafter. |
| **Common rafter -> ridge** | `TCommonRidge` | The common's head let into a recess in the ridge (the ridge is the host). |
| **Common rafter -> eave girt** | `TCommonEave` | Birdsmouth: level seat on the girt top + plumb heel; the rafter runs past as the eave tail. |
| **Purlin -> rafter** | `TPurlin` | Housed dovetail dropped into the rafter's back; the flare resists pull-out. `TJointAll`'s purlin pass batch-cuts them; the same cut lands on joist ends through its joist pass (or `TJoist`'s opt-in Joint keyword). |
| **Summer -> girt** | *Joints pane: Tusk tenon* | The classic summer joint: a soffit bearing let into the girt's bottom band plus a deep tenon riding above it, pegged. `TJointAll` batch-cuts them; the pane preset re-cuts any single one. |
| **Queen-post rafter apex** | `TQPRafter` | Where the two principal rafters meet at the peak: the male rafter's peak end seats and tenons into the host rafter (housing + tenon + optional pegs). |
| **Beam -> beam** | `TScarf` | Lengthwise end-to-end splice (Chapter 8 — it also splits the timber). |

![Figure 10-2](images/Figure%2010-2.png)

> **Figure 10-2 — The catalog on one frame.**

<!-- capture: quick-start frame SE isometric with yellow highlights + blue
labels pointing at one example of each connection actually present (girt-post,
brace, strut, rafter foot, rafter head, ridge). -->

## 10.3 Re-cutting, deleting, and what pegs do

- **Re-cut replaces.** Every joint stores an id shared by all its parts (tenon,
  mortise, housing, pegs) on both timbers. Running the cutter again on the same
  pair replaces that joint in place — change a tenon width and re-apply without
  cleanup.
- **Delete removes by id — `TJointDel` works on any pair.** Pick any two
  jointed timbers, whatever the connection type: the shared joint's features
  strip from both sides, both rebuild, and the stored recipe goes with it — a
  deleted joint never resurrects on a later `TJointSync` or a re-Generate.
  If the pair shares two joints (a girt tenoning the same post at both ends),
  the one nearest your first pick is deleted; run again for the other. The
  per-family `...Del` commands remain as conveniences; other joints on the
  same timbers are untouched. **Pegs ride the joint id** — deleting the joint
  takes its peg bores with it.
- **Batch is idempotent.** `TJointAll` skips contacts that already carry a
  joint, so re-running it after adding a girt only cuts the new work.
- **Moved a jointed timber? `TJointSync`.** Select the timbers you moved and
  the command re-cuts every joint they carry from its stored recipe: a partner
  still sharing the joint id is re-cut in place (same id); a partner that was
  *replaced* — the re-Generate case — is healed by re-attaching the joint to
  whatever the timber now touches (a fresh id). A partner that no longer
  touches at all is reported and left alone; delete that joint deliberately if
  the separation is final.
- **Regenerate replays your joinery.** When the tree's Draw replaces the
  skeleton, every joint's recipe is harvested first and re-cut onto the new
  skeleton automatically — skeleton-to-skeleton joints and free-timber halves
  alike, custom edits included. Matching is by role and position, with the
  stable label as the rescue when a parameter change relocates members (an
  eave-height move); labels renumber only when the member count changes,
  which is exactly when positions *don't* move — the two keys cover each
  other. The replay is conservative: a joint it
  cannot confidently re-pair — the member moved too far, or two candidates are
  equally near — is **reported and skipped, never guessed**. The command line
  tallies `replayed / no-contact / unmatched / ambiguous / no-recipe`; heal
  the reported ones with `TJointSync` (survivors kept their stamps) or re-cut
  with `TJointAll`. Replay restores only joints you had already cut — a new
  bent's contacts stay uncut until you cut them.

---

*Next: [Chapter 11 — The Cut List](11-cut-list.md).*
