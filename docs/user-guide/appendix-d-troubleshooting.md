# Appendix D. Troubleshooting

Symptoms, what they mean, what to do.

| Symptom | Meaning | Fix |
|---|---|---|
| **"TRoughIn: frame A is frozen -- parametric edits are locked"** (or the Draw button refuses) | The gate is working: this frame's generator is one-way locked (Ch. 6). | Edit with the managed verbs. To go parametric again, start a fresh frame tag. |
| **A rebuild changed nothing** | AutoCAD is still running the old DLL — NETLOAD cannot hot-swap. | `TVer` to see the loaded build; close AutoCAD, replace the DLL, reopen. |
| **Scribe echo: "solid geometry unavailable -- skipped"** | That timber's solid could not be read back for face extraction. | Check the solid isn't corrupted (AUDIT); re-cut its last joint; if it persists, report it — this is not expected on a healthy model. |
| **Scribe echo: "no scribe marks (plain stick) -- skipped"** | No face produced a single burnable mark. Rare — every face normally gets cut-to-length end lines. | `TScribeProbe` the timber and read the per-face verdicts before assuming a bug. |
| **A scribe label looks wrong or missing** | The annotator made a placement decision you can't see from the preview. | `TScribeProbe` — it prints, per face, what hit the face and where each label anchored (Ch. 13.4). |
| **Drawing won't save after cutting joinery** | The old SOLIDHIST issue — solids carrying history. Shouldn't happen anymore. | Report it with the command that preceded the failure. (AUDIT reports clean on this one, which is the tell.) |
| **Labels missing Dn/Up, or old-style bent labels** | The frame predates the current label conventions. | `TRelabel` (Ch. 7.4) rewrites labels in place, geometry untouched. |
| **BOM shows a stick you just changed, unchanged** | The palette doesn't auto-refresh from the model. | Click **Refresh** on the BOM palette (Ch. 11.4). |
| **A timber stopped responding correctly to TFit / TSpan / TScan** | Its stored frame desynchronized — usually a non-rigid edit (stretch, scale, non-uniform grips). | Undo the edit if you can; resize via `TSection`, refit via `TFit` (Ch. 8.3). |
| **TJointAll cut fewer joints than expected** | Contacts already carrying a joint are skipped (idempotent), and only girt-end-to-post-side bearings qualify. | The echo reports cut / skipped / failed. `TScan` to inspect what actually touches what. |
