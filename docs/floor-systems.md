# Floor Systems -- Design Direction

Direction memo for bringing floor systems into the frame model. Status: **ACCEPTED 2026-07-03**
(review calls at the end accepted as proposed). The phased roadmap at the end sequences the work;
**phases 1-3 are built** (1: 2026-07-03; 2 + 3: 2026-07-07). Build notes: joist end dovetails cut
AT PLACE TIME inside `TJoist` (sticky Joint spec, ON by default); the sill label ships as `SL-1AE`,
not `1AE-Sl` -- the `-Sl` qualifier predated the type-first label grammar, whose family prefix now
does that job (`TG-1AE` / `FG-1AE-Dn` / `SL-1AE` are already distinct); the transverse sill runs
post OUTER face to outer face (full span) so every post foot bears on its bent's sill, and the
longitudinal bay sills run post-to-post between them (corner laps stay future work); the post-foot
stub tenon is `TJointAll`'s second pass (its own sticky recipe, seeded short + unpegged).

**Amendment 2 (Robert's workflow rule, 2026-07-07): joinery is DELAYED AND DELIBERATE.** Timbers
are moved and adjusted a lot before joinery belongs on them, so nothing cuts at place time by
default: `TJoist` lands PLAIN joists (its Joint keyword is the opt-in), and the dovetails are cut
later by selecting the field and running `TJointAll` (now selection-scoped, with a joist->carrier
pass). Once applied, joinery travels with the timber; `TJointSync` re-cuts a moved timber's joints
and re-attaches orphans after a skeleton re-Generate (which now preserves all hand-placed timbers).
This supersedes phase 2's "every joist lands jointed in one command" default.

**Amendment (Robert's call at first test, 2026-07-07): sills sit BELOW the frame datum.** Section
2's "body Z = 0..D, posts shorten" is superseded: the post feet stay the y=0 datum (full-length
posts -- so the derived grid's ground test, and everything else anchored to grade, is untouched
by ticking sills on), and the sill body occupies `Ht-D..Ht`, where the leaf's new **Height** field
is the sill TOP elevation (default 0 = right under the post feet; negative drops it deeper). The
foundation interface is the sill underside at `Ht-D`. The post-foot stub tenon projects DOWN past
y=0 into the sill exactly as any end->side tenon does.

Decisions already made (scoping, 2026-07-03):
- **Design doc first**, then build in phases.
- **Sills are recipe members** (generator side; posts tenon down into them).
- **Summers are an editor path first**; a recipe toggle may come later.
- **Joist tops sit flush with their carrier** (the classic dropped-in dovetail arrangement),
  with a Drop parameter for the recessed-deck practice.

---

## 1. The trade model

A floor system is a LEVEL of the frame: a perimeter of carrying timbers at one elevation, infill
joists spanning between them, and the deck laid over. A frame has up to three kinds of level:

```
  Bent elevation -- the levels of a two-story frame:

                      o   ridge
                     / \
                    /   \                      roof system
              _____/_____\_____
             |    tie girt     |  ....... tie level        (1AE-Up)
             |                 |
        post |   floor girt    |  ....... floor level      (1AE-Dn)
             |=================|          girts carry summers + joists = SECOND floor
             |                 |
             |                 |
             |______sills______|  ....... grade/sill level
            [====foundation====]          sills carry joists = FIRST floor
```

```
  Plan of one bay's floor system (looking down):

        bent 1 girt (1AE-Dn)
     A =============================== E
     |               |                 |
     w --- joist --- s --- joist ---- w      wall girts FG-A-I / FG-E-I along A and E
     a --- joist --- u --- joist ---- a      summer runs bent-to-bent, mid-span
     l --- joist --- m --- joist ---- l      joists run wall-to-summer,
     l --- joist --- m --- joist ---- l        tops flush, dovetailed in
     |               |                 |
     A =============================== E
        bent 2 girt (2AE-Dn)
```

The pieces, in trade terms (proposed GLOSSARY.md additions -- applied only when this doc is
accepted, per the naming source-of-truth rule):

| Term | Meaning |
|---|---|
| **Sill** | timber at grade on the foundation; posts tenon down into it; first-floor joists frame into it |
| **Summer beam** (summer) | the major mid-span carrier in a bay, girt-to-girt or bent-to-bent; halves the joist span |
| **Carrier** | any timber a joist frames into: girt, summer, or sill |
| **Dropped-in (soffit) dovetail** | joist-end dovetail lowered into a flared housing in the carrier's side; resists pull-out; tops flush |
| **Tusk tenon** | a through/deep tenon with a soffit shoulder bearing below it -- the classic summer-to-girt joint |
| **Sleeper** | ground-bearing joist on grade -- NOTED, out of scope |

Existing machinery this builds on: floor girts are already recipe members (per-bent transverse
`FG` + longitudinal eave-wall girts, per-bay Height, knee braces, Dn/Up labels); `TJoist` already
places joist rows; the dovetail-housing engine (`PurlinRafterJoint`) and the box-tenon engine
(`GirtPostJoint`) already cut host-neutral joints.

---

## 2. Sills (recipe family)

Sills are grid-anchored, full-module, role-implied -- by the architecture boundary rule they are
**generator members**, exactly parallel to the floor-girt family.

**Recipe.** New member family seeded in `BentSpec.BuildDefaultTimbers` (transverse sill per
KingPost/QueenPost/HammerBeam bent) and `BaySpec.BuildDefaultTimbers` (longitudinal sill per eave
bay), following the floor-girt pattern (FrameSpec.cs:792/1014, merge mapping for bay L/R keys).
**Off by default** -- existing recipes generate unchanged until the checkbox is ticked. Truss
types get none (no wall posts to seat).

**Geometry.**
- Sill body occupies Z = 0 .. D: the UNDERSIDE of the sill is the foundation interface at Z=0,
  the sill TOP is at its depth D.
- Posts shorten to start at the sill top: the builders' `KeepAboveY(0)` becomes
  `KeepAboveY(sillTop)` (KingPostBentGraph.cs:393-419). Eave height stays measured from Z=0, so
  the frame's outer dimensions do not move when sills are switched on.
- Default section: square at the post width (e.g. 8x8 on an 8" post), editable W/D like any
  member. Length: post-face to post-face transverse; per-bay longitudinal (continuous sills with
  scarfs are a later refinement -- `TScarf` already exists for the splice).

**Joinery.** Post foot -> sill top is an end->side box tenon: the existing `GirtPostJoint` engine
contract (two frames + the post's bottom end face + a JointSpec), no new cutter. Proposed default:
short unpegged stub tenon (traditional -- gravity does the work), pegs available in the spec for
those who want them. Sill corner laps at building corners are catalogued as future work.

**Labels.** Longitudinal wall sills: letter-first family code `SL` -> `SL-A-I`, `SL-E-II`
(FamilyCode pattern, FrameGrid.cs:160-173). The transverse bent sill spans A-E in the bent plane
like the girts, so its digit-first span label needs a level qualifier to avoid colliding with the
tie ("1AE"): proposed **`1AE-Sl`** (sill level), joining the existing `-Dn`/`-Up` pair. Sills feed
`_floorGirtZ`-style level bookkeeping only if needed; grade is unique per line, so no pairing
logic is required.

**Regeneration notice.** Building this phase changes Generate output. When it lands, the saved
working model must be REBUILT to pick it up -- that will be announced explicitly at fix time.

---

## 3. Summers (editor path)

A summer is owner-anchored free-assembly infill -- **editor side**, per the architecture rule.
A recipe toggle (auto-centered per-bay summer) is parked for later.

**Placement** already works: `TSpan` between the two transverse floor girts drops the beam in.
What is missing is identity, the joint, and the label:

- **Identity:** role "Summer" -- most naturally via a Summer section preset in the TPanel palette
  (the section Type already flows into the timber's XData), so no new placement verb.
- **Joint:** summer end -> girt side. Phase one uses the box tenon + housing via `GirtPostJoint`
  (contract is two frames + mating end + spec; the pair here is orthogonal and level -- verify at
  build time that nothing in the engine assumes a vertical host). The classic **tusk tenon**
  (deep tenon + soffit shoulder bearing) is catalogued as a follow-on element combination in the
  connection-type toolkit -- an engine variant, not a new engine.
- **Label:** owner-addressed by bay: `SB-I-1` (family SB, bay I, sequence). Minted by the TAssign
  labeler described in section 4.

---

## 4. Joists (make TJoist real)

`TJoist` today places anonymous plain sticks: Type "Timber", butt ends, vertically CENTERED on the
picked faces, and never labeled. Four upgrades make joists first-class:

**(a) Identity.** TJoist writes role **"Joist"** (today `DrawBox` gets "Timber",
ManagedTimber.cs:5530). Every role-keyed system -- `TJointAll`'s role lists, `FamilyCode`, the
scribe's brace dedup -- is currently blind to joists; a real role turns those on.

**(b) Flush tops.** Joist top = carrier top plane, replacing the centered rule
(ManagedTimber.cs:5471-5472). The carrier top is derived from the picked span faces (a full side
face's upper edge IS the carrier top). A **Drop** parameter (default 0 = flush) covers the
recessed-deck practice (joists down by plank thickness). If the two carriers' tops disagree, use
the lower and report it -- flush frames should never trip this.

**(c) End joinery.** Dropped-in dovetail via the existing `PurlinRafterJoint` engine
(ManagedTimber.cs:1406) -- verified host-neutral: the cut is authored in the male member's local
frame and the host only supplies the side face, so a level girt/summer/sill host works as-is.
Cut **at place time** with a session-sticky joist spec (the `_joint` pattern), so a joist row
lands jointed in one command; the Joints pane can still re-cut or delete any single end. Joist-
tuned spec defaults are set during the build's review loop. TScan note: joint features are
invisible to `Faces()`, so the clean seat nodes joists already produce stay intact.

**(d) Labels.** Implement the owner-addressed free-timber scheme from the label conventions --
which was never actually built. `TAssign` today writes the owner tags but mints no label
(ManagedTimber.cs:3231-3235). It gains a labeler: family code from role (J for joists, SB for
summers), owner from the assignment (bay for joists/summers), sequence along the run ->
**`J-I-1..n`**. BOM and scribe then collapse identical joists to one cut mark the way braces
already collapse (generalize the brace signature, ScribeCommands.cs:210-218).

---

## 5. Floors as levels (PARKED)

One floor level per bent is a hard limit today: a single `FloorGirt:FG` entry with one Height
scalar. The generalization is known -- the bent carries a LIST of girt levels, and the Dn/Up
qualifier grows into named levels -- but it is explicitly parked: not in the first three phases,
revisit when a real multi-story frame demands it.

---

## 6. Decking

Not a timber; stays out of the model. With flush tops the deck plane is simply carrier top +
plank thickness, and the Drop parameter covers recessed decks. Note only -- no feature.

---

## 7. Phased roadmap

Each phase is its own session with its own plan; this doc only fixes the order and scope.

| Phase | Scope | Side |
|---|---|---|
| 1 | BUILT 2026-07-03: Joist identity + flush tops/Drop + TAssign labeler (J-I-n) + BOM/scribe cut-mark grouping | editor |
| 2 | BUILT 2026-07-07: Joist end dovetails (engine reuse, sticky spec, cut at place time) + Summer identity/joint/label | editor |
| 3 | BUILT 2026-07-07: Sills in the recipe + post-foot stub tenons (TJointAll sill pass) + labels (`SL-1AE`/`SL-A-I`) -- saved model rebuild announced | generator |
| 4 | BUILT 2026-07-07 (partial, Robert's go): recipe summers (Center-line bay leaf, auto-centered, `SB-C-I`) + tusk tenon (a named preset of the box-tenon kit: soffit-bearing housing + deep tenon; TJointAll summer pass + Joints pane) + Floor 0 draws the sills. STILL PARKED (own design sessions): multi-level floors, sill corner laps + continuous/scarfed sills; sleepers + decking stay non-features per sections 1/6. | mixed |

Editor phases go first deliberately: they touch nothing the generator emits, so the working model
never needs a rebuild until phase 3.

---

## Review calls (ACCEPTED as proposed, 2026-07-03)

1. Sill level qualifier spelled **`-Sl`** (`1AE-Sl`) -- happy with that, or prefer another mark?
2. Sill default section = square at post width -- right default?
3. Post-foot tenon default **unpegged** stub -- agree?
4. Joist ownership = **bay** (`J-I-1`), numbered along the run -- or would you rather own them by
   wall like commons/purlins? *(superseded by amendment 1 below: floors own their members)*
5. Joist run direction convention in the sketch (wall-to-summer, short span) -- matches your
   practice?

---

## Amendments (2026-07-03, post-acceptance)

1. **Ownership hierarchy refined (Robert's call):** Frame -> Bent -> members | Wall -> Bay ->
   members | **Floor -> members**. Floor-system members (joists, summers) are owned by a numbered
   FLOOR, bottom-up (1 = first floor), not by a bay: labels are `J-<floor>-<seq>` /
   `SB-<floor>-<seq>` (supersedes the bay-owned `J-I-1` in section 4). TAssign gained the Floor
   owner kind and a FloorTag on the timber.
2. **Pane preset renamed:** "Purlin dovetail" -> **"Housed dovetail"** -- the engine is
   host-neutral (purlin -> rafter, joist -> carrier), so the preset is named for the cut, not one
   role pair.
3. **Scribe dedup extended:** commons and purlins collapse to one drawing set like braces/joists,
   and the exported stem carries the group COUNT (`Joist_8x10_x6_face*.tsj` = cut 6 of these).
4. **TShop floor plans (Robert's call):** joist spacing is judged in PLAN, so TShop draws one
   floor-plan map per floor level -- labeled joists/summers with their connected carriers as gray
   context, bent-number bubbles along the bottom + wall-letter bubbles up the left. The in-place
   column-grid plan is now titled **Floor 0** (grid + post feet; frame sills will live there when
   built). Test case: Floor 1 at the floor-girt height.
