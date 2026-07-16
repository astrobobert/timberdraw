# 2. Installation

TimberDraw is a plugin DLL that AutoCAD loads at startup. Installing is:
get the DLL, tell AutoCAD to load it, confirm with `TVer`.

---

## 2.1 Requirements

| Requirement | Notes |
|---|---|
| **Windows** | 64-bit |
| **AutoCAD 2020** | The build target. *(Newer releases may load .NET Framework plugins but are not yet tested — see the repo README for current status.)* |
| **.NET Framework 4.8** | Usually already present on Windows 10/11 |

## 2.2 Loading the plugin

1. Get `TimberDraw.dll` (from a release, or build the repo — developers see
   [README](../../README.md)).
2. In AutoCAD, run `NETLOAD` and browse to `TimberDraw.dll`. If Windows warns
   about a downloaded file, unblock it first (file Properties -> Unblock).
3. Run `TVer` — it answers with the loaded build. That's the install working.

**Load automatically:** run `APPLOAD`, add `TimberDraw.dll` to the **Startup
Suite**, and it loads with every drawing session.

![Figure 2-1](images/Figure%202-1.png)

> **Figure 2-1 — APPLOAD with TimberDraw in the Startup Suite.**

<!-- capture: the APPLOAD dialog, Startup Suite contents showing
TimberDraw.dll. -->

## 2.3 Updating

**NETLOAD cannot hot-swap.** AutoCAD locks the DLL from the moment it loads;
to pick up a new build, close AutoCAD, replace the DLL, reopen. If behavior
looks stale after an update, `TVer` tells you which build is actually loaded.

## 2.4 Where output lands

| Output | Location |
|---|---|
| The frame model, labels, shop maps | In the drawing itself (shop maps on the **TM Shop** paper-space layout) |
| Scribe `.tsj` files | A folder you choose at export — default `Scribe\` next to the drawing |
| BOM CSV | A path you choose at export |
| Frame templates (`.framespec`) | A path you choose at Save |

---

*Next: [Chapter 3 — Concepts](03-concepts.md), the one required read.*
