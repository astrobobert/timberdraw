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
            JointFactory.RegisterDefaults();
            ManagedTransformOverrule.Enable();   // native MOVE/ROTATE keep managed frames in lockstep
        }

		void IExtensionApplication.Terminate()
		{
			ManagedTransformOverrule.Disable();
			Properties.Settings.Default.Save();
		}

		// -----------------------------------------------------------------------
		// TFrame -- node/edge frame-graph core (Milestone 1).
		// Builds a King Post bent as a graph of nodes + member edges and renders
		// plain solid-box bodies that mate at shared nodes. No joinery -- fully
		// separate from the TDraw path. Uses current Module1 params when set,
		// otherwise sensible test defaults so it runs standalone.
		// -----------------------------------------------------------------------
		[CommandMethod("TFrame")]
		public static void DrawFrame()
		{
			KPBentParams p = ReadKPParams();
			// Sync globals (harmless; useful if any reused helper reads them).
			Module1.Span = p.Span; Module1.EaveHt = p.EaveHt; Module1.Pitch = p.Pitch;
			Module1.Beta = p.Beta; Module1.Make3D = p.Make3D;

			FrameGraph g = KingPostBentGraph.Build(p);
			FrameRenderer.Draw(g);

			Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
				"\nTFrame: drew " + g.Edges.Count + " members across " + g.Nodes.Count + " nodes.");
		}

		// Geometry-first Queen Post test command: renders an all-QP frame from the live
		// palette plus QP defaults. No settings/UI yet -- the FrameSpec/tree integration is
		// a later step.
		[CommandMethod("TFrameQP")]
		public static void DrawQueenFrame()
		{
			KPBentParams p = ReadQPParams();
			Module1.Span = p.Span; Module1.EaveHt = p.EaveHt; Module1.Pitch = p.Pitch;
			Module1.Beta = p.Beta; Module1.Make3D = p.Make3D;

			FrameGraph g = KingPostBentGraph.BuildQueen(p);
			FrameRenderer.Draw(g);

			Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
				"\nTFrameQP: drew " + g.Edges.Count + " members across " + g.Nodes.Count + " nodes.");
		}

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

		// Geometry-first Hammer Beam test command (divisor 4): renders an all-HB frame from
		// the live palette plus HB defaults. No settings/UI yet.
		[CommandMethod("TFrameHB")]
		public static void DrawHammerFrame()
		{
			KPBentParams p = ReadHBParams();
			Module1.Span = p.Span; Module1.EaveHt = p.EaveHt; Module1.Pitch = p.Pitch;
			Module1.Beta = p.Beta; Module1.Make3D = p.Make3D;

			FrameGraph g = KingPostBentGraph.BuildHammer(p);
			FrameRenderer.Draw(g);

			Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
				"\nTFrameHB: drew " + g.Edges.Count + " members across " + g.Nodes.Count + " nodes.");
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

		// Geometry-first truss test commands. Full-span tie at the eave, rafters seated on it,
		// central posts re-based onto the tie. 30'x20', 12:12 test size for readability.
		[CommandMethod("TFrameKPT")]
		public static void DrawKingPostTrussFrame()
		{
			KPBentParams p = ReadKPParams();
			p.Span = 360.0; p.EaveHt = 240.0; p.Pitch = 1.0;
			Module1.Span = p.Span; Module1.EaveHt = p.EaveHt; Module1.Pitch = p.Pitch;
			Module1.Beta = p.Beta; Module1.Make3D = p.Make3D;

			FrameGraph g = KingPostBentGraph.BuildKingPostTruss(p);
			FrameRenderer.Draw(g);
			Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
				"\nTFrameKPT: drew " + g.Edges.Count + " members across " + g.Nodes.Count + " nodes.");
		}

		[CommandMethod("TFrameQPT")]
		public static void DrawQueenPostTrussFrame()
		{
			KPBentParams p = ReadQPParams();   // QP defaults: HasQueen/HasUpperGirt, QueenOffset
			p.Span = 360.0; p.EaveHt = 240.0; p.Pitch = 1.0;
			Module1.Span = p.Span; Module1.EaveHt = p.EaveHt; Module1.Pitch = p.Pitch;
			Module1.Beta = p.Beta; Module1.Make3D = p.Make3D;

			FrameGraph g = KingPostBentGraph.BuildQueenPostTruss(p);
			FrameRenderer.Draw(g);
			Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
				"\nTFrameQPT: drew " + g.Edges.Count + " members across " + g.Nodes.Count + " nodes.");
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

			// The generator owns the recipe only while it runs: clear the cross-bent scaffolding so a
			// run never inherits stale pending-mortise handles from a prior run.
			Module1.ResetCrossBentQueues();

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

			ManagedTimber.EraseGrid(db, "A");
			int drawn = ManagedFrameEmitter.Emit(g, placement, "A", out FrameGrid grid);
			grid.Draw(placement, "A");   // flat under the frame (model basis -> floor on WCS XY)

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
				catch { }
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

		// Builds a graph from the live palette values and writes it to a .tframe JSON
		// file -- the persisted node/edge "database". The file is the source of truth:
		// TFrameLoad reproduces the drawing from it alone.
		[CommandMethod("TFrameSave")]
		public static void SaveFrame()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			FrameGraph g = KingPostBentGraph.Build(ReadKPParams());
			string path = FramePath(doc);
			System.IO.File.WriteAllText(path, FrameStore.ToJson(g));
			doc?.Editor.WriteMessage("\nTFrameSave: wrote " + g.Nodes.Count + " nodes, "
				+ g.Edges.Count + " edges to " + path);
		}

		// Renders a frame purely from a stored .tframe file (no params, no generator)
		// -- proves the graph database fully describes the drawing.
		[CommandMethod("TFrameLoad")]
		public static void LoadFrame()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			string path = FramePath(doc);
			if (!System.IO.File.Exists(path))
			{
				doc?.Editor.WriteMessage("\nTFrameLoad: no file at " + path);
				return;
			}
			FrameGraph g = FrameStore.FromJson(System.IO.File.ReadAllText(path));
			FrameRenderer.Draw(g);
			doc?.Editor.WriteMessage("\nTFrameLoad: rendered " + g.Edges.Count
				+ " members from " + path);
		}

		// Reads the live King Post palette values from Settings -- the same source the
		// TDraw KP path uses. UserControl1 flushes each textbox edit to Settings on
		// leave/validate, so changes take effect immediately without a prior TDraw.
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

		// .tframe path: next to the active drawing, or temp if the drawing is unsaved.
		private static string FramePath(Document doc)
		{
			string dir = "";
			try { dir = System.IO.Path.GetDirectoryName(doc?.Name ?? ""); } catch { }
			if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.GetTempPath();
			return System.IO.Path.Combine(dir, "frame.tframe");
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
		// never discards in-progress work. The flat Frame params panel is parked behind
		// TFrameFlat; the legacy member-by-member palette behind TDrawLegacy.
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

		// Parked flat Frame params panel (the pre-tree FrameControl).
		internal static PaletteSet psFlat = null;
		[CommandMethod("TFrameFlat")]
		public static void ShowFlatFramePalette()
		{
			if (psFlat == null) {
				psFlat = new PaletteSet("TimberDraw (Flat)", "TimberDrawFlat", new Guid("4C1F7BB9-5371-4673-B579-C16F49539CC7"));
				FrameControl frameForm = new();
				psFlat.Add("Frame", frameForm);
				psFlat.MinimumSize = new System.Drawing.Size(225, 680);
				psFlat.Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton;
			}
			psFlat.Visible = true;
		}

		// Parked legacy palette (member-by-member, joinery-first draw path).
		internal static PaletteSet psLegacy = null;
		[CommandMethod("TDrawLegacy")]
		public static void ShowLegacyPalette()
		{
			if (psLegacy == null) {
				psLegacy = new PaletteSet("TimberDraw (Legacy)", "TimberDrawLegacy", new Guid("4C1F7BB9-5371-4673-B579-C16F49539CC6"));
				UserControl1 myForm = new();
				psLegacy.Add("TimberDraw", myForm);
				psLegacy.MinimumSize = new System.Drawing.Size(225, 680);
				psLegacy.Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton;
			}
			psLegacy.Visible = true;
		}

        // -----------------------------------------------------------------------
        // TRegenTimber -- Phase 2/3 parametric regeneration command
        //
        // Interactive mode (no pending params):
        //   Select a timber solid -> enter new Width -> enter new Depth
        //
        // Automated mode (TimberTag Regen button):
        //   TimberTag writes a "TRegenPending" Xrecord to NamedObjectsDictionary
        //   with handle + new dimensions + joint types, then calls SendStringToExecute.
        //   This command reads those params, clears the entry, and runs regen
        //   without any user prompts.
        // -----------------------------------------------------------------------
        [CommandMethod("TRegenTimber")]
        public static void RegenTimber()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // --- Check for pending regen written by TimberTag ---
            PendingRegenParams pending = ReadAndClearPending(db);
            if (pending.Found)
            {
                ObjectId pendId = Module1.GetObjectIdFromHandle(pending.TimberHandle);
                if (pendId.IsNull)
                {
                    ed.WriteMessage("\nTRegenTimber: pending timer handle not found in drawing.");
                    return;
                }
                Module1.DataStructure xd2 = Module1.GetXdata(pendId);
                try
                {
                    ObjectId newId = TimberFactory.Regenerate(pending.TimberHandle,
                        pending.Width, pending.Depth,
                        pending.JointNear, pending.JointFar);
                    WriteRegenResult(db, newId);
                    ed.WriteMessage(
                        "\nRegenerated " + xd2.Type + " " + xd2.BentNumber + xd2.Designation
                        + " as " + pending.Width + "x" + pending.Depth + ".");
                }
                catch (NotImplementedException nie)
                {
                    ed.WriteMessage("\n" + nie.Message);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\nRegenerate failed: " + ex.Message);
                }
                return;
            }

            // --- Interactive mode ---
            var sopts = new PromptEntityOptions("\nSelect a timber solid to regenerate: ");
            sopts.SetRejectMessage("\nMust select a 3D solid.");
            sopts.AddAllowedClass(typeof(Solid3d), true);
            PromptEntityResult er = ed.GetEntity(sopts);
            if (er.Status != PromptStatus.OK) return;

            Module1.DataStructure xd = Module1.GetXdata(er.ObjectId);
            if (string.IsNullOrEmpty(xd.Type))
            {
                ed.WriteMessage("\nSelected entity has no TimberDraw xdata. Select a timber drawn by TDraw.");
                return;
            }

            ed.WriteMessage("\nTimber: " + xd.Type + " " + xd.BentNumber + xd.Designation
                            + "  current size " + xd.Width + "x" + xd.Depth);

            var wopts = new PromptDoubleOptions("\nNew Width [" + xd.Width + "\"]: ") { AllowNone = true };
            wopts.DefaultValue = xd.Width;
            PromptDoubleResult wr = ed.GetDouble(wopts);
            if (wr.Status != PromptStatus.OK && wr.Status != PromptStatus.None) return;
            double newWidth = (wr.Status == PromptStatus.None) ? xd.Width : wr.Value;

            var dopts = new PromptDoubleOptions("\nNew Depth [" + xd.Depth + "\"]: ") { AllowNone = true };
            dopts.DefaultValue = xd.Depth;
            PromptDoubleResult dr = ed.GetDouble(dopts);
            if (dr.Status != PromptStatus.OK && dr.Status != PromptStatus.None) return;
            double newDepth = (dr.Status == PromptStatus.None) ? xd.Depth : dr.Value;

            // Joint-type prompts: Enter keeps the current type.
            string curNear = string.IsNullOrEmpty(xd.JointNear) ? "Tenon" : xd.JointNear;
            string curFar  = string.IsNullOrEmpty(xd.JointFar)  ? "Tenon" : xd.JointFar;

            var jnopts = new PromptKeywordOptions(
                "\nNear joint [Tenon/Shoulder/Butt] <" + curNear + ">: ");
            jnopts.Keywords.Add("Tenon");
            jnopts.Keywords.Add("Shoulder");
            jnopts.Keywords.Add("Butt");
            jnopts.AllowNone = true;
            PromptResult jnr = ed.GetKeywords(jnopts);
            if (jnr.Status != PromptStatus.OK && jnr.Status != PromptStatus.None) return;
            string newJointNear = (jnr.Status == PromptStatus.None) ? curNear : jnr.StringResult;

            var jfopts = new PromptKeywordOptions(
                "\nFar joint  [Tenon/Shoulder/Butt] <" + curFar + ">: ");
            jfopts.Keywords.Add("Tenon");
            jfopts.Keywords.Add("Shoulder");
            jfopts.Keywords.Add("Butt");
            jfopts.AllowNone = true;
            PromptResult jfr = ed.GetKeywords(jfopts);
            if (jfr.Status != PromptStatus.OK && jfr.Status != PromptStatus.None) return;
            string newJointFar = (jfr.Status == PromptStatus.None) ? curFar : jfr.StringResult;

            Handle timberHandle = er.ObjectId.Handle;
            try
            {
                ObjectId newId = TimberFactory.Regenerate(timberHandle, newWidth, newDepth,
                    newJointNear, newJointFar);
                WriteRegenResult(db, newId);
                ed.WriteMessage("\nRegenerated " + xd.Type + " " + xd.BentNumber + xd.Designation
                                + " as " + newWidth + "x" + newDepth
                                + "  near:" + newJointNear + " far:" + newJointFar + ".");
            }
            catch (NotImplementedException nie)
            {
                ed.WriteMessage("\n" + nie.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nRegenerate failed: " + ex.Message);
            }
        }

        // -----------------------------------------------------------------------
        // Helpers for cross-plugin pending-regen handshake via NamedObjectsDictionary
        // -----------------------------------------------------------------------

        internal const string PendingKey = "TRegenPending";
        internal const string ResultKey  = "TRegenResult";

        internal struct PendingRegenParams
        {
            public bool   Found;
            public Handle TimberHandle;
            public double Width;
            public double Depth;
            public string JointNear;
            public string JointFar;
        }

        // Write the new timber's handle to NOM["TRegenResult"] so TimberTag's
        // OnRegenCommandEnded can refresh its display using the new entity instead
        // of the erased old one.  Overwrites any stale entry from a previous call.
        internal static void WriteRegenResult(Database db, ObjectId newId)
        {
            if (newId.IsNull) return;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                if (nod.Contains(ResultKey))
                {
                    DBObject old = tr.GetObject(nod.GetAt(ResultKey), OpenMode.ForWrite);
                    old.Erase();
                    nod.Remove(ResultKey);
                }
                Xrecord xrec = new Xrecord
                {
                    Data = new ResultBuffer(
                        new TypedValue((int)DxfCode.Text, newId.Handle.ToString()))
                };
                nod.SetAt(ResultKey, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
                tr.Commit();
            }
        }

        internal const string RegenMapKey = "TRegenMap";

        // Writes the full (oldHandle -> newHandle) map to NOM["TRegenMap"] so TimberTag
        // can update every stale SS entry after TRegenTimber completes.
        // Format: one Text TypedValue per pair, value = "oldHex:newHex".
        // Overwrites any entry from a previous call.
        internal static void WriteRegenMap(Database db, List<(Handle Old, Handle New)> map)
        {
            if (map == null || map.Count == 0) return;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                if (nod.Contains(RegenMapKey))
                {
                    DBObject old = tr.GetObject(nod.GetAt(RegenMapKey), OpenMode.ForWrite);
                    old.Erase();
                    nod.Remove(RegenMapKey);
                }
                var rb = new ResultBuffer();
                foreach (var pair in map)
                    rb.Add(new TypedValue((int)DxfCode.Text,
                        pair.Old.ToString() + ":" + pair.New.ToString()));
                Xrecord xrec = new Xrecord { Data = rb };
                nod.SetAt(RegenMapKey, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
                tr.Commit();
            }
        }

        internal static PendingRegenParams ReadAndClearPending(Database db)
        {
            var result = new PendingRegenParams();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(PendingKey))
                {
                    tr.Abort();
                    return result;
                }
                nod.UpgradeOpen();
                Xrecord xrec = (Xrecord)tr.GetObject(nod.GetAt(PendingKey), OpenMode.ForRead);
                TypedValue[] vals = xrec.Data.AsArray();
                // expected layout: Text(handle) Real(w) Real(d) Text(jn) Text(jf)
                if (vals.Length >= 5)
                {
                    result.Found = true;
                    result.TimberHandle = new Handle(
                        System.Convert.ToInt64((string)vals[0].Value, 16));
                    result.Width     = (double)vals[1].Value;
                    result.Depth     = (double)vals[2].Value;
                    result.JointNear = (string)vals[3].Value;
                    result.JointFar  = (string)vals[4].Value;
                }
                // clear the entry
                xrec.UpgradeOpen();
                xrec.Erase();
                nod.Remove(PendingKey);
                tr.Commit();
            }
            return result;
        }
	}
}
