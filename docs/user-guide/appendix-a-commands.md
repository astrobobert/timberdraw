# Appendix A. Command Reference

Every command, one line, grouped by job. The palette openers (`TDraw`,
`TPanel`, `TBrowse`, `TBom`) all land on their tab of the **one TimberDraw
palette** — Frame / Assembly / Joints / Browser / Output.

## Frame design

| Command | One line |
|---|---|
| `TDraw` | Open the Frame tab (tree + properties + Generate/Freeze/templates). Ch. 5 |
| `TRoughIn` | Command-line alternate: emit one bent of the configured type at a picked point. Ch. 5 |
| `TFreeze` | The break: lock the frame's generator, one-way. Ch. 6 |

## Grid + labels

| Command | One line |
|---|---|
| `TGrid` | Redraw the structural grid, derived from the drawing. Ch. 7 |
| `TAssign` | Give free timbers an address (bent intersection / wall+bay / floor). Ch. 7 |
| `TRelabel` | Retrofit current label conventions onto an older frame. Ch. 7 |

## Editor verbs

| Command | One line |
|---|---|
| `TPanel` | Open the Assembly tab (sticky sections + brace spec + the verb bar). Ch. 8 |
| `TPlace` | Place one timber of the sticky section. Ch. 8 |
| `TSpan` | Fill the gap between two picked timbers. Ch. 8 |
| `TJoin` | Connect two picked faces (square filler or mitered knee). Ch. 8 |
| `TFit` | Trim/extend a picked end onto a target face. Ch. 8 |
| `TSection` | Re-section a timber (W x D) in place. Ch. 8 |
| `TScarf` | Split a timber in two with a scarf splice. Ch. 8 |
| `TJoist` | Place a row of plain floor joists in a bay (dovetails cut later via TJointAll). Ch. 8 |
| `TScan` | Rescan face coincidence; mark connection nodes. Ch. 8 |
| `TBrowse` | Open the Browser tab: filter, highlight, zoom, re-section, assign. Ch. 8 |
| `TUcsPlan` / `TUcsBent` / `TUcsWall` | UCS presets for comfortable placement. Ch. 8 |

## Joinery

| Command | One line |
|---|---|
| `TJoinPick` / `TJoinApply` / `TJoinClear` | The Joints pane: pick a pair, edit the element stack, cut; Clear removes the held pair's joint (Apply right after = re-snap). Ch. 10 |
| `TJoint` / `TJointDel` | Girt end -> post: tenon + housing + pegs. Ch. 10 |
| `TJointAll` | Batch-cut joinery for All or a Selection: girt-post, post-sill, summer-girt, joist-carrier passes. Ch. 10 |
| `TJointSync` | Re-cut a moved timber's joints from their stored recipes; re-attach orphans after a re-Generate. Ch. 10 |
| `TBrace` / `TBraceDel` | Knee brace: 1 1/2" barefaced tenon. Ch. 10 |
| `TStrut` / `TStrutDel` | Strut / V-strut tenon onto any host face, any angle. Ch. 10 |
| `TRafterFoot` / `TRafterFootDel` | Rafter foot let into a post side (sloped wedge). Ch. 10 |
| `TRafterHead` / `TRafterHeadDel` | Rafter head shoulder notch on the king post. Ch. 10 |
| `TRidge` / `TRidgeDel` | Ridge drop-in housing at the king-post apex. Ch. 10 |
| `TRidgeRafter` / `TRidgeRafterDel` | The same drop-in, housed into a rafter head (no-king-post bents). Ch. 10 |
| `TCommonRidge` / `TCommonRidgeDel` | Common rafter head housed into the ridge. Ch. 10 |
| `TCommonEave` / `TCommonEaveDel` | Common rafter birdsmouth on the eave girt. Ch. 10 |
| `TPurlin` / `TPurlinDel` | Purlin housed dovetail into the rafter back. Ch. 10 |
| `TQPRafter` / `TQPRafterDel` | Queen-post rafter apex: peak end tenons into the host rafter. Ch. 10 |

## Output

| Command | One line |
|---|---|
| `TBom` | Open the Output tab (the cut list); rows highlight solids; CSV export. Ch. 11 |
| `TShop` / `TShopClear` | Build / remove the shop-map set on the TM Shop layout. Ch. 12 |
| `TScribe` | Export `.tsj` scribe files for selected timbers. Ch. 13 |
| `TScribeAll` | Export the whole frame (clears the folder first). Ch. 13 |
| `TScribeProbe` | Explain one timber's per-face scribe decisions. Ch. 13 |

## Utility

| Command | One line |
|---|---|
| `TVer` | Print the loaded build (the am-I-stale check). Ch. 2 |
| `TPickFace` | Debug: pick and report one analytic face. |
| `TDiag` | Dump this session's diagnostic warnings (silently-recovered geometry/persistence failures). |

## Removed

The legacy parametric pipeline's commands (`TDrawLegacy`, `TFrameFlat`,
`TRegenTimber`, `TFrame`/`TFrameQP`/`TFrameHB`/`TFrameKPT`/`TFrameQPT`,
`TFrameSave`/`TFrameLoad`, `TSave`/`TLoad`) were **removed in July 2026**.
Drawings made by that pipeline still open — but they are edited with the
managed verbs like everything else; per-timber parametric regeneration is
gone with it.
