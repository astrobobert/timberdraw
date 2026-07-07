using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Queen Post bent for the Frame core. Geometry-first: plain half-plane bodies, no joinery.
    // Reuses the King Post builder's shared helpers (BentConnectors, AddBayMembers,
    // AddCommonRafters, AddPurlins, IntersectRayLine, AddLongFacesOneSided, PerpUp,
    // ZOffsetFor) via the partial class. The bent CONNECTORS are identical to the King Post
    // (Apex, PostTopL/R), so all bay/roof machinery and the renderer work unchanged.
    //
    // Difference from the King Post: no central king post -- the two rafters run full
    // foot->apex and butt at the centerline; two QUEEN POSTS stand at the span thirds
    // (parameterized) from the girt top up under the rafters; a STRAINING BEAM ties their
    // tops; queen STRUTS brace from each queen post up to its rafter.
    public static partial class KingPostBentGraph
    {
        // Top-level Queen Post frame: schedule of identical QP bents tied by bay members,
        // commons XOR purlins. Mirrors Build(KPBentParams) but builds Queen Post bents.
        public static FrameGraph BuildQueen(KPBentParams p)
        {
            var g = new FrameGraph();
            double[] bays = p.BaySpacings ?? new double[0];

            var conns  = new List<BentConnectors>();
            var bentZs = new List<double>();
            double z = 0;
            for (int i = 0; i <= bays.Length; i++)
            {
                bentZs.Add(z);
                conns.Add(BuildQueenBent(g, p, z));
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

        // Builds one Queen Post bent into g at building-length position bentZ. Returns the
        // connector node ids (same set as the King Post). `enabled` gates members by
        // "Role:Designation" key (null = all enabled).
        private static BentConnectors BuildQueenBent(FrameGraph g, KPBentParams p, double bentZ,
            Func<string, bool> enabled = null)
        {
            bool On(string key) => enabled == null || enabled(key);
            double hs       = p.Span / 2.0;
            double yTopFoot = p.EaveHt + p.PostD * p.Pitch;
            double apexY    = p.EaveHt + hs * p.Pitch;

            // Queen post x positions (outer/inner faces), symmetric about the center. The INNER face
            // (toward the bent center) sits QueenOffset from center; clamp to keep the post between the
            // wall post and the center line.
            double qLi = hs - p.QueenOffset;                                  // left inner face
            qLi = System.Math.Min(System.Math.Max(qLi, p.PostD + p.QueenD), hs);
            double qLo = qLi - p.QueenD;             // left outer face
            double qRi = p.Span - qLi;               // right inner face
            double qRo = p.Span - qLo;               // right outer face

            // Shared rafter face lines (XY; point + direction). Z irrelevant to the clip.
            Point3d  ltP = new Point3d(0, p.EaveHt, 0);                     Vector3d ltD = new Vector3d(1,  p.Pitch, 0);
            Point3d  lbP = new Point3d(0, p.EaveHt - p.PlumbLength, 0);     Vector3d lbD = ltD;
            Point3d  rtP = new Point3d(p.Span, p.EaveHt, 0);               Vector3d rtD = new Vector3d(1, -p.Pitch, 0);
            Point3d  rbP = new Point3d(p.Span, p.EaveHt - p.PlumbLength, 0); Vector3d rbD = rtD;

            // Nodes (Z = bentZ).
            int postBaseL = g.AddNode("PostBaseL", new Point3d(0, 0, bentZ));
            int postTopL  = g.AddNode("PostTopL",  new Point3d(0, p.EaveHt, bentZ));
            int postBaseR = g.AddNode("PostBaseR", new Point3d(p.Span - p.PostD, 0, bentZ));
            int postTopR  = g.AddNode("PostTopR",  new Point3d(p.Span - p.PostD, p.EaveHt, bentZ));
            int girtL     = g.AddNode("GirtL", new Point3d(p.PostD, p.BOG, bentZ));
            int girtR     = g.AddNode("GirtR", new Point3d(p.Span - p.PostD, p.BOG, bentZ));
            int eaveL     = g.AddNode("EaveL", new Point3d(p.PostD, yTopFoot, bentZ));
            int eaveR     = g.AddNode("EaveR", new Point3d(p.Span - p.PostD, yTopFoot, bentZ));
            int apex      = g.AddNode("Apex",  new Point3d(hs, apexY, bentZ));

            // Left/right post: top cut by the matching rafter top line (same as King Post); base at
            // y=0, the frame datum (a sill sits BELOW it).
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

            // Girt (tie): rectangle between post inner faces at girt height.
            if (On("Girt:AE"))
            g.AddEdge("Girt", girtL, girtR, p.GirtW, p.GirtD, "AE").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(p.PostD), HalfPlane.KeepLeftOfX(p.Span - p.PostD),
                HalfPlane.KeepAboveY(p.BOG),     HalfPlane.KeepBelowY(p.BOG + p.GirtD)
            });

            // Rafters run FULL foot->apex and LAP at the ridge: the left rafter's bottom plane
            // is the contact. Left rafter is cut at the ridge by the RIGHT rafter's top plane
            // (its bottom face runs out to the right rafter's top); the right rafter is cut by
            // the LEFT rafter's bottom plane (its top face stops at the left rafter's bottom).
            if (On("Rafter:A"))
            g.AddEdge("Rafter", eaveL, apex, p.RafterW, p.RafterD, "A").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(ltP, ltD), HalfPlane.KeepAboveLine(lbP, lbD),
                HalfPlane.KeepRightOfX(p.PostD),   HalfPlane.KeepBelowLine(rtP, rtD)
            });
            if (On("Rafter:E"))
            g.AddEdge("Rafter", eaveR, apex, p.RafterW, p.RafterD, "E").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(rtP, rtD), HalfPlane.KeepAboveLine(rbP, rbD),
                HalfPlane.KeepBelowLine(lbP, lbD), HalfPlane.KeepLeftOfX(p.Span - p.PostD)
            });

            // Queen posts: from girt top (TOG) up, top tucked under the rafter underside.
            if (p.HasQueen && On("Queen:B"))
            {
                int qbL = g.AddNode("QueenBaseL", new Point3d(qLo, p.TOG, bentZ));
                int qtL = g.AddNode("QueenTopL",  new Point3d(qLo, (p.EaveHt - p.PlumbLength) + qLi * p.Pitch, bentZ));
                g.AddEdge("QueenPost", qbL, qtL, p.QueenW, p.QueenD, "B").Planes.AddRange(new[]
                {
                    HalfPlane.KeepRightOfX(qLo), HalfPlane.KeepLeftOfX(qLi),
                    HalfPlane.KeepAboveY(p.TOG), HalfPlane.KeepBelowLine(lbP, ltD)
                });
            }
            if (p.HasQueen && On("Queen:D"))
            {
                int qbR = g.AddNode("QueenBaseR", new Point3d(qRo, p.TOG, bentZ));
                int qtR = g.AddNode("QueenTopR",  new Point3d(qRo, (p.EaveHt - p.PlumbLength) + qLi * p.Pitch, bentZ));
                g.AddEdge("QueenPost", qbR, qtR, p.QueenW, p.QueenD, "D").Planes.AddRange(new[]
                {
                    HalfPlane.KeepRightOfX(qRi), HalfPlane.KeepLeftOfX(qRo),
                    HalfPlane.KeepAboveY(p.TOG), HalfPlane.KeepBelowLine(rbP, rtD)
                });
            }

            // Straining beam: horizontal between the queen-post inner faces, top 6" below the
            // rafter underside at the inner face (6" tenon spacing, as in the legacy QPUpperGirt).
            if (p.HasUpperGirt && On("UpperGirt:BD"))
            {
                double beamTop = (p.EaveHt - p.PlumbLength) + qLi * p.Pitch - 6.0;
                double beamBot = beamTop - p.UpperGirtD;
                int sbL = g.AddNode("StrainL", new Point3d(qLi, beamTop, bentZ));
                int sbR = g.AddNode("StrainR", new Point3d(qRi, beamTop, bentZ));
                g.AddEdge("Girt", sbL, sbR, p.UpperGirtW, p.UpperGirtD, "BD").Planes.AddRange(new[]
                {
                    HalfPlane.KeepRightOfX(qLi), HalfPlane.KeepLeftOfX(qRi),
                    HalfPlane.KeepBelowY(beamTop), HalfPlane.KeepAboveY(beamBot)
                });
            }

            // Knee braces: wall posts -> tie girt (same recipe as the King Post), AND queen
            // posts -> straining beam. Foot = leg DOWN the post (vertical); Head = leg ALONG the
            // girt (horizontal). Body INSIDE the triangle (hypotenuse = outer face) via OffsetToward.
            if (p.HasBrace && p.BraceFoot > 0 && p.BraceHead > 0)
            {
                double foot = p.BraceFoot;   // down the post (vertical)
                double head = p.BraceHead;   // along the girt (horizontal)

                // ---- Wall-post braces (post inner face -> tie girt bottom) ----
                if (On("Brace:AB"))
                {
                    Point3d bla = new Point3d(p.PostD, p.BOG - foot, 0);
                    Point3d blb = new Point3d(p.PostD + head, p.BOG, 0);
                    Point3d cL  = new Point3d(p.PostD, p.BOG, 0);
                    FrameEdge bl = g.AddEdge("Brace",
                        g.AddNode("BraceLPost", new Point3d(bla.X, bla.Y, bentZ)),
                        g.AddNode("BraceLGirt", new Point3d(blb.X, blb.Y, bentZ)),
                        p.BraceW, p.BraceD, "AB");
                    bl.Planes.Add(HalfPlane.KeepRightOfX(p.PostD));
                    bl.Planes.Add(HalfPlane.KeepBelowY(p.BOG));
                    AddLongFacesOneSided(bl, bla, blb, p.BraceD, OffsetToward(bla, blb, cL));
                    bl.ZOffset = ZOffsetFor(Math.Min(p.PostW, p.GirtW), p.BraceW, p.PlaceOf("Brace:A"));

                    Point3d bra = new Point3d(p.Span - p.PostD, p.BOG - foot, 0);
                    Point3d brb = new Point3d(p.Span - p.PostD - head, p.BOG, 0);
                    Point3d cR  = new Point3d(p.Span - p.PostD, p.BOG, 0);
                    FrameEdge br = g.AddEdge("Brace",
                        g.AddNode("BraceRPost", new Point3d(bra.X, bra.Y, bentZ)),
                        g.AddNode("BraceRGirt", new Point3d(brb.X, brb.Y, bentZ)),
                        p.BraceW, p.BraceD, "AB");
                    br.Planes.Add(HalfPlane.KeepLeftOfX(p.Span - p.PostD));
                    br.Planes.Add(HalfPlane.KeepBelowY(p.BOG));
                    AddLongFacesOneSided(br, bra, brb, p.BraceD, OffsetToward(bra, brb, cR));
                    br.ZOffset = ZOffsetFor(Math.Min(p.PostW, p.GirtW), p.BraceW, p.PlaceOf("Brace:E"));
                }

                // ---- Queen-post braces (queen post inner face -> straining beam bottom) ----
                if (p.HasQueen && p.HasUpperGirt && On("QueenBrace:BD"))
                {
                    double beamBot = (p.EaveHt - p.PlumbLength) + qLi * p.Pitch - 6.0 - p.UpperGirtD;

                    Point3d qbla = new Point3d(qLi, beamBot - foot, 0);
                    Point3d qblb = new Point3d(qLi + head, beamBot, 0);
                    Point3d qcL  = new Point3d(qLi, beamBot, 0);
                    FrameEdge qbl = g.AddEdge("Brace",
                        g.AddNode("QBraceLPost", new Point3d(qbla.X, qbla.Y, bentZ)),
                        g.AddNode("QBraceLGirt", new Point3d(qblb.X, qblb.Y, bentZ)),
                        p.BraceW, p.BraceD, "BD");
                    qbl.Planes.Add(HalfPlane.KeepRightOfX(qLi));
                    qbl.Planes.Add(HalfPlane.KeepBelowY(beamBot));
                    AddLongFacesOneSided(qbl, qbla, qblb, p.BraceD, OffsetToward(qbla, qblb, qcL));
                    qbl.ZOffset = ZOffsetFor(Math.Min(p.QueenW, p.UpperGirtW), p.BraceW, p.PlaceOf("QueenBrace:B"));

                    Point3d qbra = new Point3d(qRi, beamBot - foot, 0);
                    Point3d qbrb = new Point3d(qRi - head, beamBot, 0);
                    Point3d qcR  = new Point3d(qRi, beamBot, 0);
                    FrameEdge qbr = g.AddEdge("Brace",
                        g.AddNode("QBraceRPost", new Point3d(qbra.X, qbra.Y, bentZ)),
                        g.AddNode("QBraceRGirt", new Point3d(qbrb.X, qbrb.Y, bentZ)),
                        p.BraceW, p.BraceD, "BD");
                    qbr.Planes.Add(HalfPlane.KeepLeftOfX(qRi));
                    qbr.Planes.Add(HalfPlane.KeepBelowY(beamBot));
                    AddLongFacesOneSided(qbr, qbra, qbrb, p.BraceD, OffsetToward(qbra, qbrb, qcR));
                    qbr.ZOffset = ZOffsetFor(Math.Min(p.QueenW, p.UpperGirtW), p.BraceW, p.PlaceOf("QueenBrace:D"));
                }
            }

            // Queen struts: queen-post outer face (TOG+6) up-and-out at StrutAngle to the
            // rafter underside. Mirror of the King Post strut recipe.
            if (p.HasStrut && p.StrutD > 0 && On("Strut:S"))
            {
                double theta = p.StrutAngle * Math.PI / 180.0;
                double ct = Math.Cos(theta), st = Math.Sin(theta);

                Point3d asL  = new Point3d(qLo, p.TOG + 6.0, 0);
                Point3d hitL = IntersectRayLine(asL, new Vector3d(-ct, st, 0), lbP, ltD);
                int aL = g.AddNode("QStrutLBase",   new Point3d(asL.X, asL.Y, bentZ));
                int bL = g.AddNode("QStrutLRafter", new Point3d(hitL.X, hitL.Y, bentZ));
                FrameEdge sL = g.AddEdge("Strut", aL, bL, p.StrutW, p.StrutD, "S");
                sL.Planes.Add(HalfPlane.KeepLeftOfX(qLo));         // queen post outer face
                sL.Planes.Add(HalfPlane.KeepBelowLine(lbP, ltD));  // rafter underside
                AddLongFacesOneSided(sL, asL, hitL, p.StrutD, PerpUp(asL.GetVectorTo(hitL)));
                sL.ZOffset = ZOffsetFor(Math.Min(p.QueenW, p.RafterW), p.StrutW, p.PlaceOf("Strut:A"));

                Point3d asR  = new Point3d(qRo, p.TOG + 6.0, 0);
                Point3d hitR = IntersectRayLine(asR, new Vector3d(ct, st, 0), rbP, rtD);
                int aR = g.AddNode("QStrutRBase",   new Point3d(asR.X, asR.Y, bentZ));
                int bR = g.AddNode("QStrutRRafter", new Point3d(hitR.X, hitR.Y, bentZ));
                FrameEdge sR = g.AddEdge("Strut", aR, bR, p.StrutW, p.StrutD, "S");
                sR.Planes.Add(HalfPlane.KeepRightOfX(qRo));        // queen post outer face
                sR.Planes.Add(HalfPlane.KeepBelowLine(rbP, rtD));  // rafter underside
                AddLongFacesOneSided(sR, asR, hitR, p.StrutD, PerpUp(asR.GetVectorTo(hitR)));
                sR.ZOffset = ZOffsetFor(Math.Min(p.QueenW, p.RafterW), p.StrutW, p.PlaceOf("Strut:E"));
            }

            AddFloorGirt(g, p, bentZ, enabled);
            AddSill(g, p, bentZ, enabled);
            return new BentConnectors { Apex = apex, PostTopL = postTopL, PostTopR = postTopR };
        }
    }
}
