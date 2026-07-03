using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // King Post Truss and Queen Post Truss for the Frame core (geometry-first). A truss is its
    // bent counterpart with the wall POSTS replaced by a full-span TIE at the eave: the rafters
    // extend to the wall corners and bear on the tie, and the central king/queen posts (+ struts
    // / straining beam / queen braces) re-base from TOG onto the tie (EaveHt). Plain bearing
    // rafter feet (square seat on the tie top, no notch). Connectors are the same (Apex,
    // PostTopL/R), so all bay/roof machinery and the renderer reuse unchanged.
    public static partial class KingPostBentGraph
    {
        public static FrameGraph BuildKingPostTruss(KPBentParams p) => BuildTrussFrame(p, true);
        public static FrameGraph BuildQueenPostTruss(KPBentParams p) => BuildTrussFrame(p, false);

        private static FrameGraph BuildTrussFrame(KPBentParams p, bool king)
        {
            var g = new FrameGraph();
            double[] bays = p.BaySpacings ?? new double[0];

            var conns  = new List<BentConnectors>();
            var bentZs = new List<double>();
            double z = 0;
            for (int i = 0; i <= bays.Length; i++)
            {
                bentZs.Add(z);
                conns.Add(king ? BuildKingPostTrussBent(g, p, z) : BuildQueenPostTrussBent(g, p, z));
                if (i < bays.Length) z += bays[i];
            }

            for (int i = 0; i + 1 < conns.Count; i++)
            {
                AddBayMembers(g, p, conns[i], conns[i + 1]);
                if (p.UseCommons) AddCommonRafters(g, p, bentZs[i], bentZs[i + 1]);
                else              AddPurlins(g, p, bentZs[i], bentZs[i + 1]);
            }

            return g;
        }

        // Full-span tie at the eave (top at EaveHt), section GirtW x GirtD -- replaces the posts.
        private static void AddTrussTie(FrameGraph g, KPBentParams p, double bentZ, Func<string, bool> On)
        {
            if (!On("Girt:AE")) return;
            int a = g.AddNode("TieL", new Point3d(0, p.EaveHt, bentZ));
            int b = g.AddNode("TieR", new Point3d(p.Span, p.EaveHt, bentZ));
            g.AddEdge("Girt", a, b, p.GirtW, p.GirtD, "AE").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(0),                HalfPlane.KeepLeftOfX(p.Span),
                HalfPlane.KeepAboveY(p.EaveHt - p.GirtD), HalfPlane.KeepBelowY(p.EaveHt)
            });
        }

        private static BentConnectors BuildKingPostTrussBent(FrameGraph g, KPBentParams p, double bentZ,
            Func<string, bool> enabled = null)
        {
            bool On(string key) => enabled == null || enabled(key);
            double hs       = p.Span / 2.0;
            double xPeakL   = hs - p.KpostD / 2.0;
            double xPeakR   = hs + p.KpostD / 2.0;
            double yTopPeak = p.EaveHt + (hs - p.KpostD / 2.0) * p.Pitch;
            double apexY    = p.EaveHt + hs * p.Pitch;

            Point3d  ltP = new Point3d(0, p.EaveHt, 0);                     Vector3d ltD = new Vector3d(1,  p.Pitch, 0);
            Point3d  lbP = new Point3d(0, p.EaveHt - p.PlumbLength, 0);     Vector3d lbD = ltD;
            Point3d  rtP = new Point3d(p.Span, p.EaveHt, 0);               Vector3d rtD = new Vector3d(1, -p.Pitch, 0);
            Point3d  rbP = new Point3d(p.Span, p.EaveHt - p.PlumbLength, 0); Vector3d rbD = rtD;

            int cornerL = g.AddNode("CornerL", new Point3d(0, p.EaveHt, bentZ));
            int cornerR = g.AddNode("CornerR", new Point3d(p.Span, p.EaveHt, bentZ));
            int peakL   = g.AddNode("PeakL", new Point3d(xPeakL, yTopPeak, bentZ));
            int peakR   = g.AddNode("PeakR", new Point3d(xPeakR, yTopPeak, bentZ));
            int apex    = g.AddNode("Apex",  new Point3d(hs, apexY, bentZ));

            AddTrussTie(g, p, bentZ, On);

            // Rafters: foot at the wall corner (x=0/Span), bearing on the tie (KeepAboveY EaveHt),
            // peak cut by the king post (KP recipe).
            if (On("Rafter:A"))
            g.AddEdge("Rafter", cornerL, peakL, p.RafterW, p.RafterD, "A").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(ltP, ltD), HalfPlane.KeepAboveLine(lbP, lbD),
                HalfPlane.KeepRightOfX(0),         HalfPlane.KeepLeftOfX(xPeakL),
                HalfPlane.KeepAboveY(p.EaveHt)
            });
            if (On("Rafter:E"))
            g.AddEdge("Rafter", cornerR, peakR, p.RafterW, p.RafterD, "E").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(rtP, rtD), HalfPlane.KeepAboveLine(rbP, rbD),
                HalfPlane.KeepLeftOfX(p.Span),     HalfPlane.KeepRightOfX(xPeakR),
                HalfPlane.KeepAboveY(p.EaveHt)
            });

            // King post: KP recipe, base on the tie (EaveHt).
            if (On("KingPost:C"))
            g.AddEdge("KingPost", g.AddNode("KPostBase", new Point3d(xPeakL, p.EaveHt, bentZ)), apex,
                p.KpostW, p.KpostD, "C").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(xPeakL), HalfPlane.KeepLeftOfX(xPeakR),
                HalfPlane.KeepAboveY(p.EaveHt),
                HalfPlane.KeepBelowLine(ltP, ltD), HalfPlane.KeepBelowLine(rtP, rtD)
            });

            // Struts (king-post face, EaveHt+6 -> rafter underside) + vert struts (tie -> hit).
            double theta = p.StrutAngle * Math.PI / 180.0;
            double ct = Math.Cos(theta), st = Math.Sin(theta);
            Point3d asL  = new Point3d(xPeakL, p.EaveHt + 6.0, 0);
            Point3d asR  = new Point3d(xPeakR, p.EaveHt + 6.0, 0);
            Point3d hitL = IntersectRayLine(asL, new Vector3d(-ct, st, 0), lbP, ltD);
            Point3d hitR = IntersectRayLine(asR, new Vector3d( ct, st, 0), rbP, rtD);

            if (p.HasStrut && p.StrutD > 0 && On("Strut:S"))
            {
                FrameEdge sL = g.AddEdge("Strut",
                    g.AddNode("StrutLKpost",  new Point3d(asL.X, asL.Y, bentZ)),
                    g.AddNode("StrutLRafter", new Point3d(hitL.X, hitL.Y, bentZ)),
                    p.StrutW, p.StrutD, "S");
                sL.Planes.Add(HalfPlane.KeepLeftOfX(xPeakL));
                sL.Planes.Add(HalfPlane.KeepBelowLine(lbP, ltD));
                AddLongFacesOneSided(sL, asL, hitL, p.StrutD, PerpUp(asL.GetVectorTo(hitL)));
                sL.ZOffset = ZOffsetFor(Math.Min(p.KpostW, p.RafterW), p.StrutW, p.PlaceOf("Strut:A"));

                FrameEdge sR = g.AddEdge("Strut",
                    g.AddNode("StrutRKpost",  new Point3d(asR.X, asR.Y, bentZ)),
                    g.AddNode("StrutRRafter", new Point3d(hitR.X, hitR.Y, bentZ)),
                    p.StrutW, p.StrutD, "S");
                sR.Planes.Add(HalfPlane.KeepRightOfX(xPeakR));
                sR.Planes.Add(HalfPlane.KeepBelowLine(rbP, rtD));
                AddLongFacesOneSided(sR, asR, hitR, p.StrutD, PerpUp(asR.GetVectorTo(hitR)));
                sR.ZOffset = ZOffsetFor(Math.Min(p.KpostW, p.RafterW), p.StrutW, p.PlaceOf("Strut:E"));
            }

            if (p.HasVStrut && p.VStrutD > 0 && On("VStrut:V"))
            {
                Point3d vbL = new Point3d(hitL.X, p.EaveHt, 0);
                FrameEdge vL = g.AddEdge("VStrut",
                    g.AddNode("VStrutLGirt",   new Point3d(vbL.X, vbL.Y, bentZ)),
                    g.AddNode("VStrutLRafter", new Point3d(hitL.X, hitL.Y, bentZ)),
                    p.VStrutW, p.VStrutD, "V");
                vL.Planes.Add(HalfPlane.KeepAboveY(p.EaveHt));
                vL.Planes.Add(HalfPlane.KeepBelowLine(lbP, ltD));
                AddLongFacesOneSided(vL, vbL, hitL, p.VStrutD, new Vector3d(hitL.X < hs ? -1 : 1, 0, 0));
                vL.ZOffset = ZOffsetFor(Math.Min(p.GirtW, p.RafterW), p.VStrutW, p.PlaceOf("VStrut:A"));

                Point3d vbR = new Point3d(hitR.X, p.EaveHt, 0);
                FrameEdge vR = g.AddEdge("VStrut",
                    g.AddNode("VStrutRGirt",   new Point3d(vbR.X, vbR.Y, bentZ)),
                    g.AddNode("VStrutRRafter", new Point3d(hitR.X, hitR.Y, bentZ)),
                    p.VStrutW, p.VStrutD, "V");
                vR.Planes.Add(HalfPlane.KeepAboveY(p.EaveHt));
                vR.Planes.Add(HalfPlane.KeepBelowLine(rbP, rtD));
                AddLongFacesOneSided(vR, vbR, hitR, p.VStrutD, new Vector3d(hitR.X < hs ? -1 : 1, 0, 0));
                vR.ZOffset = ZOffsetFor(Math.Min(p.GirtW, p.RafterW), p.VStrutW, p.PlaceOf("VStrut:E"));
            }

            return new BentConnectors { Apex = apex, PostTopL = cornerL, PostTopR = cornerR };
        }

        private static BentConnectors BuildQueenPostTrussBent(FrameGraph g, KPBentParams p, double bentZ,
            Func<string, bool> enabled = null)
        {
            bool On(string key) => enabled == null || enabled(key);
            double hs    = p.Span / 2.0;
            double apexY = p.EaveHt + hs * p.Pitch;
            double qLi = hs - p.QueenOffset;   // inner face (toward center), from bent center
            qLi = System.Math.Min(System.Math.Max(qLi, p.PostD + p.QueenD), hs);
            double qLo = qLi - p.QueenD;
            double qRi = p.Span - qLi;
            double qRo = p.Span - qLo;

            Point3d  ltP = new Point3d(0, p.EaveHt, 0);                     Vector3d ltD = new Vector3d(1,  p.Pitch, 0);
            Point3d  lbP = new Point3d(0, p.EaveHt - p.PlumbLength, 0);     Vector3d lbD = ltD;
            Point3d  rtP = new Point3d(p.Span, p.EaveHt, 0);               Vector3d rtD = new Vector3d(1, -p.Pitch, 0);
            Point3d  rbP = new Point3d(p.Span, p.EaveHt - p.PlumbLength, 0); Vector3d rbD = rtD;

            int cornerL = g.AddNode("CornerL", new Point3d(0, p.EaveHt, bentZ));
            int cornerR = g.AddNode("CornerR", new Point3d(p.Span, p.EaveHt, bentZ));
            int apex    = g.AddNode("Apex",  new Point3d(hs, apexY, bentZ));

            AddTrussTie(g, p, bentZ, On);

            // Rafters: full foot->apex, LAP at the ridge (QP recipe), foot at the wall corner,
            // bearing on the tie.
            if (On("Rafter:A"))
            g.AddEdge("Rafter", cornerL, apex, p.RafterW, p.RafterD, "A").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(ltP, ltD), HalfPlane.KeepAboveLine(lbP, lbD),
                HalfPlane.KeepRightOfX(0),         HalfPlane.KeepBelowLine(rtP, rtD),
                HalfPlane.KeepAboveY(p.EaveHt)
            });
            if (On("Rafter:E"))
            g.AddEdge("Rafter", cornerR, apex, p.RafterW, p.RafterD, "E").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(rtP, rtD), HalfPlane.KeepAboveLine(rbP, rbD),
                HalfPlane.KeepLeftOfX(p.Span),     HalfPlane.KeepBelowLine(lbP, lbD),
                HalfPlane.KeepAboveY(p.EaveHt)
            });

            // Queen posts at the span thirds, base on the tie (EaveHt), top under the rafter.
            if (p.HasQueen && On("Queen:B"))
            g.AddEdge("QueenPost",
                g.AddNode("QueenBaseL", new Point3d(qLo, p.EaveHt, bentZ)),
                g.AddNode("QueenTopL",  new Point3d(qLo, (p.EaveHt - p.PlumbLength) + qLi * p.Pitch, bentZ)),
                p.QueenW, p.QueenD, "B").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(qLo), HalfPlane.KeepLeftOfX(qLi),
                HalfPlane.KeepAboveY(p.EaveHt), HalfPlane.KeepBelowLine(lbP, ltD)
            });
            if (p.HasQueen && On("Queen:D"))
            g.AddEdge("QueenPost",
                g.AddNode("QueenBaseR", new Point3d(qRo, p.EaveHt, bentZ)),
                g.AddNode("QueenTopR",  new Point3d(qRo, (p.EaveHt - p.PlumbLength) + qLi * p.Pitch, bentZ)),
                p.QueenW, p.QueenD, "D").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(qRi), HalfPlane.KeepLeftOfX(qRo),
                HalfPlane.KeepAboveY(p.EaveHt), HalfPlane.KeepBelowLine(rbP, rtD)
            });

            // Straining beam (top 6" below the rafter underside at the queen inner face).
            double beamTop = (p.EaveHt - p.PlumbLength) + qLi * p.Pitch - 6.0;
            double beamBot = beamTop - p.UpperGirtD;
            if (p.HasUpperGirt && On("UpperGirt:BD"))
            g.AddEdge("Girt",
                g.AddNode("StrainL", new Point3d(qLi, beamTop, bentZ)),
                g.AddNode("StrainR", new Point3d(qRi, beamTop, bentZ)),
                p.UpperGirtW, p.UpperGirtD, "BD").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(qLi), HalfPlane.KeepLeftOfX(qRi),
                HalfPlane.KeepBelowY(beamTop), HalfPlane.KeepAboveY(beamBot)
            });

            // Queen braces: queen post inner face -> straining beam bottom (no wall posts to brace).
            // Foot = leg DOWN the queen post; Head = leg ALONG the straining beam. Body INSIDE the
            // triangle (hypotenuse = outer face) via OffsetToward.
            if (p.HasBrace && p.BraceFoot > 0 && p.BraceHead > 0 && p.HasQueen && p.HasUpperGirt && On("QueenBrace:BD"))
            {
                double foot = p.BraceFoot;   // down the queen post (vertical)
                double head = p.BraceHead;   // along the straining beam (horizontal)

                Point3d la = new Point3d(qLi, beamBot - foot, 0);
                Point3d lb = new Point3d(qLi + head, beamBot, 0);
                Point3d lc = new Point3d(qLi, beamBot, 0);
                FrameEdge bl = g.AddEdge("Brace",
                    g.AddNode("QBraceLPost", new Point3d(la.X, la.Y, bentZ)),
                    g.AddNode("QBraceLGirt", new Point3d(lb.X, lb.Y, bentZ)),
                    p.BraceW, p.BraceD, "BD");
                bl.Planes.Add(HalfPlane.KeepRightOfX(qLi));
                bl.Planes.Add(HalfPlane.KeepBelowY(beamBot));
                AddLongFacesOneSided(bl, la, lb, p.BraceD, OffsetToward(la, lb, lc));
                bl.ZOffset = ZOffsetFor(Math.Min(p.QueenW, p.UpperGirtW), p.BraceW, p.PlaceOf("QueenBrace:B"));

                Point3d ra = new Point3d(qRi, beamBot - foot, 0);
                Point3d rb = new Point3d(qRi - head, beamBot, 0);
                Point3d rc = new Point3d(qRi, beamBot, 0);
                FrameEdge br = g.AddEdge("Brace",
                    g.AddNode("QBraceRPost", new Point3d(ra.X, ra.Y, bentZ)),
                    g.AddNode("QBraceRGirt", new Point3d(rb.X, rb.Y, bentZ)),
                    p.BraceW, p.BraceD, "BD");
                br.Planes.Add(HalfPlane.KeepLeftOfX(qRi));
                br.Planes.Add(HalfPlane.KeepBelowY(beamBot));
                AddLongFacesOneSided(br, ra, rb, p.BraceD, OffsetToward(ra, rb, rc));
                br.ZOffset = ZOffsetFor(Math.Min(p.QueenW, p.UpperGirtW), p.BraceW, p.PlaceOf("QueenBrace:D"));
            }

            // Queen struts: queen-post outer face (EaveHt+6) up-and-out to the rafter underside.
            if (p.HasStrut && p.StrutD > 0 && On("Strut:S"))
            {
                double theta = p.StrutAngle * Math.PI / 180.0;
                double ct = Math.Cos(theta), st = Math.Sin(theta);

                Point3d asL  = new Point3d(qLo, p.EaveHt + 6.0, 0);
                Point3d hitL = IntersectRayLine(asL, new Vector3d(-ct, st, 0), lbP, ltD);
                FrameEdge sL = g.AddEdge("Strut",
                    g.AddNode("QStrutLBase",   new Point3d(asL.X, asL.Y, bentZ)),
                    g.AddNode("QStrutLRafter", new Point3d(hitL.X, hitL.Y, bentZ)),
                    p.StrutW, p.StrutD, "S");
                sL.Planes.Add(HalfPlane.KeepLeftOfX(qLo));
                sL.Planes.Add(HalfPlane.KeepBelowLine(lbP, ltD));
                AddLongFacesOneSided(sL, asL, hitL, p.StrutD, PerpUp(asL.GetVectorTo(hitL)));
                sL.ZOffset = ZOffsetFor(Math.Min(p.QueenW, p.RafterW), p.StrutW, p.PlaceOf("Strut:A"));

                Point3d asR  = new Point3d(qRo, p.EaveHt + 6.0, 0);
                Point3d hitR = IntersectRayLine(asR, new Vector3d(ct, st, 0), rbP, rtD);
                FrameEdge sR = g.AddEdge("Strut",
                    g.AddNode("QStrutRBase",   new Point3d(asR.X, asR.Y, bentZ)),
                    g.AddNode("QStrutRRafter", new Point3d(hitR.X, hitR.Y, bentZ)),
                    p.StrutW, p.StrutD, "S");
                sR.Planes.Add(HalfPlane.KeepRightOfX(qRo));
                sR.Planes.Add(HalfPlane.KeepBelowLine(rbP, rtD));
                AddLongFacesOneSided(sR, asR, hitR, p.StrutD, PerpUp(asR.GetVectorTo(hitR)));
                sR.ZOffset = ZOffsetFor(Math.Min(p.QueenW, p.RafterW), p.StrutW, p.PlaceOf("Strut:E"));
            }

            return new BentConnectors { Apex = apex, PostTopL = cornerL, PostTopR = cornerR };
        }
    }
}
