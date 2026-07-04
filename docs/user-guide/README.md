# TimberDraw User Guide

Guide for timber framers using TimberDraw. Chapters follow the framer's workflow:
design -> freeze -> edit -> joinery -> shop output -> laser. The plan and style rules
live in [OUTLINE.md](OUTLINE.md); trade terms and parameter names come from
[GLOSSARY.md](../../GLOSSARY.md) (the single source of truth).

## Chapters

| # | Chapter | Status |
|---|---|---|
| 1 | [What TimberDraw Is](01-what-timberdraw-is.md) | drafted, figures pending |
| 2 | [Installation](02-installation.md) | drafted, figures pending |
| 3 | [Concepts: How TimberDraw Thinks](03-concepts.md) | drafted, figures pending |
| 4 | [Quick Start: A Frame in Fifteen Minutes](04-quick-start.md) | drafted, figures pending |
| 5 | [The Frame Editor (`TDraw`)](05-frame-editor.md) | drafted, figures pending |
| 6 | [The Break: `TFreeze`](06-freeze.md) | drafted, figures pending |
| 7 | [The Structural Grid and Labels](07-grid-labels.md) | drafted, figures pending |
| 8 | [The Assembly Palette and the Editor Verbs](08-editor-verbs.md) | drafted, figures pending |
| 9 | [Joinery Concepts](09-joinery-concepts.md) | drafted, figures pending |
| 10 | [Cutting Joints](10-cutting-joints.md) | drafted, figures pending |
| 11 | [The Cut List (`TBom`)](11-cut-list.md) | drafted, figures pending |
| 12 | [Shop Drawings (`TShop`)](12-shop-drawings.md) | drafted, figures pending |
| 13 | [Scribe Export (`TScribe` / `TScribeAll`)](13-scribe-export.md) | drafted, figures pending |
| 14 | [TimberScribe: Burning the Timber](14-burning.md) | drafted, figures pending |
| A | [Command Reference](appendix-a-commands.md) | drafted |
| B | [Glossary](appendix-b-glossary.md) | pointer to GLOSSARY.md |
| C | [The .tsj File Format](appendix-c-tsj.md) | drafted (spec pending in timberscribe repo) |
| D | [Troubleshooting](appendix-d-troubleshooting.md) | drafted |

## Conventions in these files

- **The worked example** is the quick-start frame everywhere: two king-post bents,
  16' span, 12' eave height, 8:12 pitch, one bay.
- **Figures** are placeholders until the batch capture pass. Each placeholder is a
  blockquote starting `**Figure N-M**` with a `[capture: ...]` spec (view, commands
  to run first, what to annotate). Image files go in `img/` named `fig-NN-MM.png`
  (e.g. `img/fig-03-01.png`).
- Capture standard: light background, consistent SE isometric unless the spec says
  otherwise, annotations in blue/yellow only (never red-vs-green).
- Dimensions in inches with framer's fractions (1/2", not 0.5").
