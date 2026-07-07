using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Hammer Beam bent for the Frame core (geometry-first, divisor 4). Plain half-plane
    // bodies, no joinery. Reuses the King Post builder's shared helpers (BentConnectors,
    // AddBayMembers, AddCommonRafters, AddPurlins) via the partial class; the connectors are
    // identical to the King Post so all bay/roof machinery and the renderer work unchanged.
    //
    // Differences from the King Post:
    //  - NO full-width tie girt -- the hammer beams replace it.
    //  - The king post is SHORT: it sits on the COLLAR, not at TOG (KeepAboveY(collarTop)).
    //  - New members: hammer beams (eave-level cantilevers), hammer posts (on the beam inner
    //    end, up to the rafter underside), and the collar tying the hammer-post inner faces.
    public static partial class KingPostBentGraph
    {
        // Top-level Hammer Beam frame (mirror of Build/BuildQueen).
        public static FrameGraph BuildHammer(KPBentParams p)
        {
            var g = new FrameGraph();
            double[] bays = p.BaySpacings ?? new double[0];

            var conns  = new List<BentConnectors>();
            var bentZs = new List<double>();
            double z = 0;
            for (int i = 0; i <= bays.Length; i++)
            {
                bentZs.Add(z);
                conns.Add(BuildHammerBent(g, p, z));
                if (i < bays.Length) z += bays[i];
            }

            for (int i = 0; i + 1 < conns.Count; i++)
            {
                AddBayMembers(g, p, conns[i], conns[i + 1]);
                AddFloorBayMembers(g, p, bentZs[i], bentZs[i + 1]);
                if (p.UseCommons) AddCommonRafters(g, p, bentZs[i], bentZs[i + 1]);
                else              AddPurlins(g, p, bentZs[i], bentZs[i + 1]);
            }

            return g;
        }

        // Builds one Hammer Beam bent (divisor 4) at building-length position bentZ. Returns
        // the connector node ids (same set as the King Post).
        private static BentConnectors BuildHammerBent(FrameGraph g, KPBentParams p, double bentZ,
            Func<string, bool> enabled = null)
        {
            bool On(string key) => enabled == null || enabled(key);
            double hs       = p.Span / 2.0;
            double xPeakL   = hs - p.KpostD / 2.0;
            double xPeakR   = hs + p.KpostD / 2.0;
            double yTopFoot = p.EaveHt + p.PostD * p.Pitch;
            double yTopPeak = p.EaveHt + (hs - p.KpostD / 2.0) * p.Pitch;
            double apexY    = p.EaveHt + hs * p.Pitch;

            // Shared rafter face lines.
            Point3d  ltP = new Point3d(0, p.EaveHt, 0);                     Vector3d ltD = new Vector3d(1,  p.Pitch, 0);
            Point3d  lbP = new Point3d(0, p.EaveHt - p.PlumbLength, 0);     Vector3d lbD = ltD;
            Point3d  rtP = new Point3d(p.Span, p.EaveHt, 0);               Vector3d rtD = new Vector3d(1, -p.Pitch, 0);
            Point3d  rbP = new Point3d(p.Span, p.EaveHt - p.PlumbLength, 0); Vector3d rbD = rtD;

            // Nodes.
            int postBaseL = g.AddNode("PostBaseL", new Point3d(0, 0, bentZ));
            int postTopL  = g.AddNode("PostTopL",  new Point3d(0, p.EaveHt, bentZ));
            int postBaseR = g.AddNode("PostBaseR", new Point3d(p.Span - p.PostD, 0, bentZ));
            int postTopR  = g.AddNode("PostTopR",  new Point3d(p.Span - p.PostD, p.EaveHt, bentZ));
            int eaveL     = g.AddNode("EaveL", new Point3d(p.PostD, yTopFoot, bentZ));
            int peakL     = g.AddNode("PeakL", new Point3d(xPeakL, yTopPeak, bentZ));
            int eaveR     = g.AddNode("EaveR", new Point3d(p.Span - p.PostD, yTopFoot, bentZ));
            int peakR     = g.AddNode("PeakR", new Point3d(xPeakR, yTopPeak, bentZ));
            int apex      = g.AddNode("Apex",  new Point3d(hs, apexY, bentZ));

            // Posts (no full-width tie girt -- hammer beams replace it); base at y=0, the frame
            // datum (a sill sits BELOW it).
            if (On("Post:A"))
            g.AddEdge("Post", postBaseL, postTopL, p.PostW, p.PostD, "A").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(0), HalfPlane.KeepLeftOfX(p.PostD),
                HalfPlane.KeepAboveY(0),   HalfPlane.KeepBelowLine(ltP, ltD)
            });
            if (On("Post:E"))
            g.AddEdge("Post", postBaseR, postTopR, p.PostW, p.PostD, "E").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(p.Span - p.PostD), HalfPlane.KeepLeftOfX(p.Span),
                HalfPlane.KeepAboveY(0),                  HalfPlane.KeepBelowLine(rtP, rtD)
            });

            // Rafters: KP recipe (foot -> peak, cut by the king post at xPeakL/xPeakR).
            if (On("Rafter:A"))
            g.AddEdge("Rafter", eaveL, peakL, p.RafterW, p.RafterD, "A").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(ltP, ltD), HalfPlane.KeepAboveLine(lbP, lbD),
                HalfPlane.KeepRightOfX(p.PostD),   HalfPlane.KeepLeftOfX(xPeakL)
            });
            if (On("Rafter:E"))
            g.AddEdge("Rafter", eaveR, peakR, p.RafterW, p.RafterD, "E").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(rtP, rtD), HalfPlane.KeepAboveLine(rbP, rbD),
                HalfPlane.KeepRightOfX(xPeakR),    HalfPlane.KeepLeftOfX(p.Span - p.PostD)
            });

            // Stacked tiers: 1 for divisor 4, 2 for divisor 6 (beam+post climbing toward center).
            double hbLength = (p.Span - 2.0 * p.PostD - p.KpostD) / p.HBDivisor;
            int    tiers    = Math.Max(1, p.HBDivisor / 2 - 1);

            // Per tier: a hammer beam (eave-level cantilever, rising hbLength*Pitch per tier)
            // and a hammer post on its inner end, up to the rafter underside. Both sides.
            for (int i = 1; i <= tiers; i++)
            {
                double beamBot = p.BOG + (i - 1) * hbLength * p.Pitch;
                double beamTop = beamBot + p.HBeamD;

                double bLin = p.PostD + (i - 1) * hbLength;   // beam outer (post side)
                double bLix = p.PostD + i * hbLength;         // beam inner = hammer post inner face
                if (p.HasHBeam && On("HBeam:A"))
                g.AddEdge("HBeam",
                    g.AddNode("HBeamL", new Point3d(bLin, beamBot, bentZ)),
                    g.AddNode("HBeamL", new Point3d(bLix, beamBot, bentZ)),
                    p.HBeamW, p.HBeamD, "A").Planes.AddRange(new[]
                {
                    HalfPlane.KeepRightOfX(bLin), HalfPlane.KeepLeftOfX(bLix),
                    HalfPlane.KeepAboveY(beamBot), HalfPlane.KeepBelowY(beamTop)
                });
                if (p.HasHPost && On("HPost:A"))
                g.AddEdge("HPost",
                    g.AddNode("HPostL", new Point3d(bLix - p.HPostD, beamTop, bentZ)),
                    g.AddNode("HPostL", new Point3d(bLix, (p.EaveHt - p.PlumbLength) + bLix * p.Pitch, bentZ)),
                    p.HPostW, p.HPostD, "A").Planes.AddRange(new[]
                {
                    HalfPlane.KeepRightOfX(bLix - p.HPostD), HalfPlane.KeepLeftOfX(bLix),
                    HalfPlane.KeepAboveY(beamTop), HalfPlane.KeepBelowLine(lbP, ltD)
                });

                double bRix = p.Span - p.PostD - i * hbLength;        // beam inner = hammer post inner face
                double bRin = p.Span - p.PostD - (i - 1) * hbLength;  // beam outer (post side)
                if (p.HasHBeam && On("HBeam:E"))
                g.AddEdge("HBeam",
                    g.AddNode("HBeamR", new Point3d(bRix, beamBot, bentZ)),
                    g.AddNode("HBeamR", new Point3d(bRin, beamBot, bentZ)),
                    p.HBeamW, p.HBeamD, "E").Planes.AddRange(new[]
                {
                    HalfPlane.KeepRightOfX(bRix), HalfPlane.KeepLeftOfX(bRin),
                    HalfPlane.KeepAboveY(beamBot), HalfPlane.KeepBelowY(beamTop)
                });
                if (p.HasHPost && On("HPost:E"))
                g.AddEdge("HPost",
                    g.AddNode("HPostR", new Point3d(bRix, beamTop, bentZ)),
                    g.AddNode("HPostR", new Point3d(bRix, (p.EaveHt - p.PlumbLength) + (p.Span - bRix) * p.Pitch, bentZ)),
                    p.HPostW, p.HPostD, "E").Planes.AddRange(new[]
                {
                    HalfPlane.KeepRightOfX(bRix), HalfPlane.KeepLeftOfX(bRix + p.HPostD),
                    HalfPlane.KeepAboveY(beamTop), HalfPlane.KeepBelowLine(rbP, rtD)
                });
            }

            // Collar between the innermost hammer-post inner faces; king post sits on it.
            double innerLx   = p.PostD + tiers * hbLength;
            double innerRx   = p.Span - p.PostD - tiers * hbLength;
            double collarTop = p.BOG + p.HBeamD + tiers * hbLength * p.Pitch;
            double collarBot = collarTop - p.CollarD;
            if (p.HasCollar && On("Collar:AE"))
            g.AddEdge("Girt",
                g.AddNode("CollarL", new Point3d(innerLx, collarTop, bentZ)),
                g.AddNode("CollarR", new Point3d(innerRx, collarTop, bentZ)),
                p.CollarW, p.CollarD, "AE").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(innerLx), HalfPlane.KeepLeftOfX(innerRx),
                HalfPlane.KeepBelowY(collarTop), HalfPlane.KeepAboveY(collarBot)
            });

            // Knee braces: one under each horizontal at the vertical below it -- post -> beam1,
            // hammer post i -> beam(i+1) (intermediate tiers), and innermost hammer post ->
            // collar. Local helper keeps both sides consistent (dir = +1 left toward center, -1 right).
            // Foot = leg DOWN the vertical; Head = leg ALONG the horizontal. Body INSIDE the
            // triangle (hypotenuse = outer face) via OffsetToward.
            if (p.HasBrace && p.BraceFoot > 0 && p.BraceHead > 0)
            {
                double foot = p.BraceFoot;   // down the post/hammer post (vertical)
                double head = p.BraceHead;   // along the beam/collar (horizontal)
                // refW = NARROWER of the vertical / horizontal it connects; key = its placement key.
                void KneeBrace(double faceX, double horizBot, int dir, string desig, double refW, string key)
                {
                    Point3d a = new Point3d(faceX, horizBot - foot, 0);
                    Point3d b = new Point3d(faceX + dir * head, horizBot, 0);
                    Point3d c = new Point3d(faceX, horizBot, 0);
                    FrameEdge e = g.AddEdge("Brace",
                        g.AddNode("HBrace", new Point3d(a.X, a.Y, bentZ)),
                        g.AddNode("HBrace", new Point3d(b.X, b.Y, bentZ)),
                        p.BraceW, p.BraceD, desig);
                    e.Planes.Add(dir > 0 ? HalfPlane.KeepRightOfX(faceX) : HalfPlane.KeepLeftOfX(faceX));
                    e.Planes.Add(HalfPlane.KeepBelowY(horizBot));
                    AddLongFacesOneSided(e, a, b, p.BraceD, OffsetToward(a, b, c));
                    e.ZOffset = ZOffsetFor(refW, p.BraceW, p.PlaceOf(key));
                }

                // Post -> beam1 (corner at the eave, BOG): narrower of post / hammer beam.
                if (p.HasHBeam && On("Brace:AB"))
                {
                    double rw = Math.Min(p.PostW, p.HBeamW);
                    KneeBrace(p.PostD, p.BOG, +1, "AB", rw, "Brace:A");
                    KneeBrace(p.Span - p.PostD, p.BOG, -1, "AB", rw, "Brace:E");
                }
                // Hammer post i -> beam(i+1) for the intermediate tiers: narrower of hammer post / beam.
                if (p.HasHBeam && p.HasHPost && On("Brace:AB"))
                for (int i = 1; i < tiers; i++)
                {
                    double nextBeamBot = p.BOG + i * hbLength * p.Pitch;
                    double rw = Math.Min(p.HPostW, p.HBeamW);
                    KneeBrace(p.PostD + i * hbLength, nextBeamBot, +1, "AB", rw, "Brace:A");
                    KneeBrace(p.Span - p.PostD - i * hbLength, nextBeamBot, -1, "AB", rw, "Brace:E");
                }
                // Innermost hammer post -> collar: narrower of hammer post / collar.
                if (p.HasHPost && p.HasCollar && On("CollarBrace:AE"))
                {
                    double rw = Math.Min(p.HPostW, p.CollarW);
                    KneeBrace(innerLx, collarBot, +1, "AE", rw, "CollarBrace:A");
                    KneeBrace(innerRx, collarBot, -1, "AE", rw, "CollarBrace:E");
                }
            }

            // King post: KP recipe but base raised to the collar top (short king post).
            if (On("KingPost:C"))
            g.AddEdge("KingPost",
                g.AddNode("KPostBase", new Point3d(xPeakL, collarTop, bentZ)),
                apex,
                p.KpostW, p.KpostD, "C").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(xPeakL), HalfPlane.KeepLeftOfX(xPeakR),
                HalfPlane.KeepAboveY(collarTop),
                HalfPlane.KeepBelowLine(ltP, ltD), HalfPlane.KeepBelowLine(rtP, rtD)
            });

            AddFloorGirt(g, p, bentZ, enabled);
            AddSill(g, p, bentZ, enabled);
            return new BentConnectors { Apex = apex, PostTopL = postTopL, PostTopR = postTopR };
        }
    }
}
