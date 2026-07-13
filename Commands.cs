using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

[assembly: CommandClass(typeof(TimberDraw.Commands))]
namespace TimberDraw
{
	public class Commands : Autodesk.AutoCAD.Runtime.IExtensionApplication
	{
		void IExtensionApplication.Initialize()
        {
            ManagedTransformOverrule.Enable();   // native MOVE/ROTATE keep managed frames in lockstep
        }

		void IExtensionApplication.Terminate()
		{
			ManagedTransformOverrule.Disable();
			Properties.Settings.Default.Save();
		}

		// RETIRED 2026-07-06 (Phase C, Robert's disposition): the frame-graph bring-up test
		// commands TFrame / TFrameQP / TFrameHB / TFrameKPT / TFrameQPT, the .tframe dump pair
		// TFrameSave / TFrameLoad, the parked palettes TFrameFlat / TDrawLegacy, the .tproj pair
		// TSave / TLoad (ProjectFile.cs deleted), and the legacy regen command TRegenTimber.
		// The tree editor (TDraw) + managed verbs cover all of it. The legacy generator
		// internals (Bent\/Bay\/... , TimberFactory) remain as dead code pending the deep purge.

		// Queen Post params: the King Post live values plus QP-specific defaults (geometry-
		// first; no dedicated settings yet). Queen posts reuse the post section, the straining
		// beam reuses the girt section, and the position defaults to the span thirds.
		private static KPBentParams ReadQPParams()
		{
			KPBentParams p = ReadKPParams();
			p.HasQueen      = true;
			p.HasUpperGirt  = true;
			p.QueenOffset = p.Span / 6.0 - p.PostD;   // center to inner face; matches the old 1/3-of-span look (queen depth = PostD)
			p.QueenW        = p.PostW;   p.QueenD      = p.PostD;
			p.UpperGirtW    = p.GirtW;   p.UpperGirtD  = p.GirtD;
			return p;
		}

		// Hammer Beam params: King Post live values plus HB-specific defaults (geometry-first;
		// no dedicated settings yet). Hammer beam + collar reuse the girt section, hammer posts
		// reuse the post section, divisor 4.
		private static KPBentParams ReadHBParams()
		{
			KPBentParams p = ReadKPParams();
			// Test-size overrides (HB needs a larger frame to read clearly): 30' x 20', 12:12.
			p.Span = 360.0; p.EaveHt = 240.0; p.Pitch = 1.0;
			p.FloorGirtHt = p.EaveHt * 0.5;   // keep the floor mid-height in the test frame
			p.HasHBeam  = true;  p.HasHPost = true;  p.HasCollar = true;
			p.HBDivisor = 6;   // stacked-tier test (4 = single tier)
			p.HBeamW = p.GirtW;  p.HBeamD = p.GirtD;
			p.HPostW = p.PostW;  p.HPostD = p.PostD;
			p.CollarW = p.GirtW; p.CollarD = p.GirtD;
			return p;
		}

		// TRoughIn -- Phase 2 ROUGH-IN: build the parametric bent graph (type from the RoughInType
		// setting), prompt a base point (current UCS), and emit MANAGED timbers via
		// ManagedFrameEmitter so the result is immediately editable by the managed verbs. Bridges
		// the parametric generator into the managed-timber model.
		[CommandMethod("TRoughIn")]
		public static void DrawRoughIn()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			// One-way gate: once frame A is frozen the parametric phase is over -- never silently
			// re-emit over its hand edits.
			if (FrameRegistry.IsFrozen(db, "A"))
			{
				ed.WriteMessage("\nTRoughIn: frame A is frozen -- parametric edits are locked. " +
					"(Re-seed a fresh frame to continue parametrically.)");
				return;
			}

			string type = Properties.Settings.Default.RoughInType;
			KPBentParams p = ReadRoughInParams(type);
			FrameGraph g = (p != null) ? BuildRoughInGraph(type, p) : null;
			if (g == null) { ed.WriteMessage("\nTRoughIn: unknown bent type '" + type + "'."); return; }

			PromptPointResult res = ed.GetPoint("\nBase point for the rough-in bent: ");
			if (res.Status != PromptStatus.OK) return;

			// Treat the graph's coordinates as expressed in the current UCS with origin at the
			// picked point, then map to WCS (same convention as TPlace). The whole bent rotates with
			// the UCS; managed members land WCS-native and editable.
			Matrix3d ucs = ed.CurrentUserCoordinateSystem;
			Point3d baseWcs = res.Value.TransformBy(ucs);
			CoordinateSystem3d cs = ucs.CoordinateSystem3d;
			Matrix3d placement = Matrix3d.AlignCoordinateSystem(
				Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
				baseWcs, cs.Xaxis, cs.Yaxis, cs.Zaxis);
			placement = ManagedFrameEmitter.Compose(placement);   // stand the frame up Z-up (model basis)

			// Clear the prior skeleton before re-emitting (this was missing -- a second TRoughIn
			// stacked duplicates; only the tree's Draw button erased). Same rule as the tree:
			// EraseFrame keeps every hand-placed (free-assembly / floor-owned) timber.
			ManagedTimber.EraseFrame(db, "A");
			ManagedTimber.EraseGrid(db, "A");
			int drawn = ManagedFrameEmitter.Emit(g, placement, "A", out FrameGrid grid);
			grid.Draw(placement, "A");   // flat under the frame (model basis -> floor on WCS XY)
			ManagedCommands.RelabelBraces(db);   // authoritative brace symbols (*, **) by size+shape

			// Store the recipe on the frame (pre-break, frozen=false): bent type + seed params + the
			// placement that put it in WCS. TFreeze later flips Frozen; a future re-seed reads this.
			FrameRegistry.Save(db, "A", new FrameRecord
			{
				Type = type, Params = p, Placement = placement.ToArray(), Frozen = false
			});

			ed.WriteMessage("\nTRoughIn (" + type + "): emitted " + drawn + " managed timbers across "
				+ g.Nodes.Count + " nodes (grid " + grid.ColX.Count + " cols x " + grid.BentZ.Count + " bents).");
		}

		// TFreeze -- the BREAK / handoff. Lock a frame's parametric palette: afterward the generator
		// (TRoughIn) refuses to re-emit the frame and its skeleton timbers carry on as the managed-
		// editor truth. Geometry is untouched (the timbers are already managed); only the gate flips.
		// One-way; the escape hatch is to re-seed a fresh frame from the stored recipe (deferred).
		[CommandMethod("TFreeze")]
		public static void FreezeFrame()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptResult fr = ed.GetString(
				new PromptStringOptions("\nFrame tag to freeze: ") { DefaultValue = "A", AllowSpaces = false });
			if (fr.Status != PromptStatus.OK) return;
			string frame = string.IsNullOrWhiteSpace(fr.StringResult) ? "A" : fr.StringResult.Trim();

			if (FrameRegistry.IsFrozen(db, frame))
			{ ed.WriteMessage("\nTFreeze: frame " + frame + " is already frozen."); return; }

			if (ManagedTimber.EnumerateFrameFrames(db, frame).Count == 0)
			{ ed.WriteMessage("\nTFreeze: nothing to freeze in frame " + frame + "."); return; }

			FrameRegistry.SetFrozen(db, frame, true);
			ed.WriteMessage("\nTFreeze: frame " + frame +
				" frozen -- parametric palette locked; edit via the managed verbs.");
		}

		// Draw the STRUCTURAL GRID (lines + lettered/numbered bubbles) for the current frame, on demand.
		// Rebuilds the graph from the auto-saved frame spec (the multi-bent frame) -- else a single bent
		// from settings -- clears the old grid for that frame, and draws the new one flat under the frame
		// (model basis -> floor on WCS XY at Z=0). Draws the grid only, no timbers.
		[CommandMethod("TGrid")]
		public static void DrawStructuralGrid()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			if (doc == null) return;
			Editor ed = doc.Editor;

			// DRAWING-DERIVED grid: scan the drawing's managed timbers for this frame (parametric +
			// TPlace'd subs assigned via TAssign), so adding/removing a sub re-letters/re-numbers. Only
			// timbers that MEET THE FLOOR draw a line.
			string frameTag = "A";
			string json = Properties.Settings.Default.FrameSpecJson;
			if (!string.IsNullOrWhiteSpace(json))
			{
				try { frameTag = string.IsNullOrWhiteSpace(FrameSpecStore.FromJson(json).FrameTag) ? "A"
					: FrameSpecStore.FromJson(json).FrameTag.Trim(); }
				catch (System.Exception ex)
				{ Diag.Warn("TGrid", "FrameSpecJson unreadable, frame tag defaults to A: " + ex.Message); }
			}

			Matrix3d placement = ManagedFrameEmitter.Compose(ed.CurrentUserCoordinateSystem);  // model basis
			FrameGrid grid = FrameGrid.BuildFromDrawing(doc.Database, frameTag, placement);
			ManagedTimber.EraseGrid(doc.Database, frameTag);
			grid.Draw(placement, frameTag);   // flat under the frame
			ed.WriteMessage("\nTGrid: frame " + frameTag + " -- " + grid.ColX.Count + " columns x "
				+ grid.BentZ.Count + " bents.");
		}

		// Read the rough-in seed params for a bent type (mirrors BuildRoughInGraph's dispatch). Split
		// out so TRoughIn holds the EXACT params it emitted -- the recipe stored on the frame and the
		// graph that's drawn come from one read, no double-read drift. Returns null for unknown types.
		internal static KPBentParams ReadRoughInParams(string type)
		{
			switch ((type ?? "").Trim())
			{
				case "KingPost":       return ReadKPParams();
				case "QueenPost":      return ReadQPParams();
				case "HammerBeam":     return ReadHBParams();
				case "KingPostTruss":  return ReadKPParams();
				case "QueenPostTruss": return ReadQPParams();
				default:               return null;
			}
		}

		// Dispatch a rough-in bent type to its Build, from the already-read params. Returns null for
		// an unrecognized type (or null params).
		private static FrameGraph BuildRoughInGraph(string type, KPBentParams p)
		{
			if (p == null) return null;
			switch ((type ?? "").Trim())
			{
				case "KingPost":       return KingPostBentGraph.Build(p);
				case "QueenPost":      return KingPostBentGraph.BuildQueen(p);
				case "HammerBeam":     return KingPostBentGraph.BuildHammer(p);
				case "KingPostTruss":  return KingPostBentGraph.BuildKingPostTruss(p);
				case "QueenPostTruss": return KingPostBentGraph.BuildQueenPostTruss(p);
				default:               return null;
			}
		}

		// Reads the King Post seed values from Settings. (The legacy palettes that edited
		// these fields are retired -- the recipe UI is the tree editor; these Settings now
		// only seed fresh FrameSpecs and the TRoughIn path.)
		internal static KPBentParams ReadKPParams()
		{
			var s = TimberDraw.Properties.Settings.Default;
			double prun = s.RoofPitchRun;
			return new KPBentParams
			{
				Span    = s.Span,
				EaveHt  = s.EaveHt,
				Pitch   = (prun != 0) ? s.RoofPitchRise / prun : 0.5,
				PostW   = s.KPPostWidth,   PostD   = s.KPPostDepth,
				GirtW   = s.KPGirtWidth,   GirtD   = s.KPGirtDepth,
				RafterW = s.KPRafterWidth, RafterD = s.KPRafterDepth,
				KpostW  = s.KPKpostWidth,  KpostD  = s.KPKpostDepth,
				RidgeW  = s.RidgeWidth,    RidgeD  = s.RidgeDepth,
				BraceW  = s.KPBraceWidth,  BraceD  = s.KPBraceDepth,
				BraceLength = s.KPBraceLength,
				BraceAngle  = s.KPBraceAngle > 0 ? s.KPBraceAngle : 45.0,
				HasBrace    = s.KPHasBrace,
				StrutW  = s.KPStrutWidth,  StrutD  = s.KPStrutDepth,
				HasStrut    = s.KPHasStrut,
				VStrutW = s.KPVStrutWidth, VStrutD = s.KPVStrutDepth,
				HasVStrut   = s.KPHasVStrut,
				StrutAngle  = s.KPStrutAngle > 0 ? s.KPStrutAngle : 45.0,
				OffsetType  = (int)s.BraceStrutPlacement,
				// Bay SPACING (center-to-center, face-to-same-face). N spacings = N+1 bents.
				// Parsed from the BaySchedule CSV (Frame params UI grid).
				BaySpacings = ParseBaySchedule(s.BaySchedule),
				UseCommons    = !s.HasPurlins, // roof mode: commons unless HasPurlins is set.
				CommonMode    = s.CommonMode,      // 0 = by count, 1 = center-to-center spacing.
				CommonSpacing = s.CommonSpacing,   // requested on-center (mode 1).
				CommonCount   = s.CommonCount,     // commons per bay (mode 0).
				CommonW = s.CommonWidth,   CommonD = s.CommonDepth,
				PurlinMode    = s.PurlinMode,      // 0 = by count, 1 = on-center spacing along slope.
				PurlinSpacing = s.PurlinSpacing,   // requested on-center along the slope (mode 1).
				PurlinCount   = s.PurlinCount,     // purlins per slope per bay (mode 0).
				PurlinW = s.PurlinWidth,   PurlinD = s.PurlinDepth,
				// Shared floor girt (geometry-first defaults; FloorGirtHt is proportional).
				HasFloorGirt = true, HasFloorBrace = true,
				FloorGirtW = s.KPGirtWidth, FloorGirtD = s.KPGirtDepth,
				FloorGirtHt = (s.EaveHt > 0 ? s.EaveHt : 120.0) * 0.5,
				GirtDrop = 6.0,   // tie sits 6" below the rafter feet by default (the minimum)
				Make3D  = s.Make3D
			};
		}

		// Parses the BaySchedule CSV ("96,144") into a per-bay spacing array
		// (center-to-center). N spacings -> N+1 bents. Blank/garbage entries are
		// skipped; an empty result falls back to a single 96" bay so TFrame always
		// has at least two bents to draw.
		private static double[] ParseBaySchedule(string csv)
		{
			var list = new List<double>();
			if (!string.IsNullOrWhiteSpace(csv))
			{
				foreach (string tok in csv.Split(','))
				{
					if (double.TryParse(tok.Trim(), out double v) && v > 0)
						list.Add(v);
				}
			}
			if (list.Count == 0) list.Add(96.0);
			return list.ToArray();
		}

		// .framespec path: the FrameSpec instance model, next to the active drawing.
		private static string FrameSpecPath(Document doc)
		{
			string dir = "";
			try { dir = System.IO.Path.GetDirectoryName(doc?.Name ?? ""); } catch { }
			if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.GetTempPath();
			return System.IO.Path.Combine(dir, "frame.framespec");
		}

		// Writes the FrameSpec JSON next to the drawing; returns the path. Used by the tree UI.
		internal static string SaveFrameSpecToFile(FrameSpec spec)
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			string path = FrameSpecPath(doc);
			System.IO.File.WriteAllText(path, FrameSpecStore.ToJson(spec));
			return path;
		}

		// Reads the FrameSpec JSON from next to the drawing; null if absent. Used by the tree UI.
		internal static FrameSpec LoadFrameSpecFromFile()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			string path = FrameSpecPath(doc);
			if (!System.IO.File.Exists(path)) return null;
			return FrameSpecStore.FromJson(System.IO.File.ReadAllText(path));
		}

		// TDraw -- opens the shell on the Frame tab (the FrameSpec tree editor). The tree
		// opens EMPTY (start a frame with New, or Load a .framespec); re-invoking TDraw
		// never discards in-progress work.
		[CommandMethod("TDraw")]
		public static void ShowPalette()
		{
			Shell.Show(Shell.Tab.Frame);
		}

		// TPanel -- opens the shell on the Assembly tab (sticky sections + verb buttons +
		// orientation presets; the Joints pane is the neighboring tab). The verbs drive the
		// TPlace/TSpan/TJoin/TFit/TScarf/TScan commands via SendStringToExecute.
		[CommandMethod("TPanel")]
		public static void ShowTimberPanel()
		{
			Shell.Show(Shell.Tab.Assembly);
			Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
				"\nTimberDraw build " + BuildStamp() + " -- TPanel ready.");
		}

		// TBrowse -- opens the shell on the Browser tab (the WPF assign + review surface:
		// bound list of managed timbers, select-to-highlight, double-click zooms).
		[CommandMethod("TBrowse")]
		public static void ShowFrameBrowser()
		{
			Shell.Show(Shell.Tab.Browser);
		}

		// Build timestamp of the loaded DLL (its file write time) -- so a stale NETLOAD is obvious.
		// NETLOAD cannot hot-swap an assembly already loaded in-session; reopen AutoCAD to pick up a build.
		internal static string BuildStamp()
		{
			try
			{
				string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
				return System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
			}
			catch { return "unknown"; }
		}

		// Prints the loaded build time so you can confirm a fresh NETLOAD.
		[CommandMethod("TVer")]
		public static void ShowVersion()
		{
			Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
				"\nTimberDraw build " + BuildStamp() + ".");
		}

	}
}
