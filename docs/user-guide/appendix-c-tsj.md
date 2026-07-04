# Appendix C. The `.tsj` File Format

A `.tsj` file is a small **JSON** document describing the burn paths for **one
face of one timber**: linework (joinery profiles, cut-to-length lines) and
text marks (labels, depths, bevels), in **inches**, with the origin at the
face's **upper-left corner** as the laser sees it. Files are named
`<stem>_faceN.tsj`, N = 1–4 in RS order (Chapter 13.3).

The authoritative specification is
[TSJ_SPEC.md in the timberscribe repository](https://github.com/astrobobert/timberscribe/blob/master/TSJ_SPEC.md),
kept beside the parser that consumes it.

The schema is kept byte-compatible with the original TimberTag exporter, so
older `.tsj` files burn identically.
