using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(TimberDraw.ProjectFileCommands))]

namespace TimberDraw
{
    // -----------------------------------------------------------------------
    // Data model
    // -----------------------------------------------------------------------

    public class BentEntry
    {
        [JsonPropertyName("index")]       public int    Index              { get; set; }
        [JsonPropertyName("trussType")]   public int    TrussType          { get; set; }
        [JsonPropertyName("span")]        public double Span               { get; set; }
        [JsonPropertyName("eaveHt")]      public double EaveHt             { get; set; }
        [JsonPropertyName("pitchRise")]   public double PitchRise          { get; set; }
        [JsonPropertyName("pitchRun")]    public double PitchRun           { get; set; }
        [JsonPropertyName("wallNumber")]  public int    WallNumber         { get; set; }
        [JsonPropertyName("make3D")]      public bool   Make3D             { get; set; }
        [JsonPropertyName("hasJoinery")]  public bool   HasJoinery         { get; set; }
        [JsonPropertyName("deletePoly")]  public bool   DeletePolylines    { get; set; }
        [JsonPropertyName("offsetType")]  public int    OffsetType         { get; set; }
        [JsonPropertyName("flrGirtHt")]   public double FlrGirtHt          { get; set; }
        [JsonPropertyName("hbDivisor")]   public int    HBDivisor          { get; set; }
    }

    public class BayEntry
    {
        [JsonPropertyName("leftBentIndex")]  public int    LeftBentIndex     { get; set; }
        [JsonPropertyName("rightBentIndex")] public int    RightBentIndex    { get; set; }
        [JsonPropertyName("bayWidth")]       public double BayWidth          { get; set; }
        [JsonPropertyName("genBayMembers")]  public bool   GenerateBayMembers { get; set; }
    }

    public class BentProjectFile
    {
        [JsonPropertyName("version")] public string           Version { get; set; } = "1.0";
        [JsonPropertyName("bents")]   public List<BentEntry>  Bents   { get; set; } = new List<BentEntry>();
        [JsonPropertyName("bays")]    public List<BayEntry>   Bays    { get; set; } = new List<BayEntry>();
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    public class ProjectFileCommands
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        [CommandMethod("TSave")]
        public static void TSave()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "Timber Project|*.tproj";
                dlg.Title  = "Save Timber Project";
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                var s = Properties.Settings.Default;
                var bent = new BentEntry
                {
                    Index           = s.BentNumber,
                    TrussType       = s.TrussType,
                    Span            = s.Span,
                    EaveHt          = s.EaveHt,
                    PitchRise       = s.RoofPitchRise,
                    PitchRun        = s.RoofPitchRun,
                    WallNumber      = s.WallNumber,
                    Make3D          = s.Make3D,
                    HasJoinery      = s.HasJoinery,
                    DeletePolylines = s.DeletePolylines,
                    OffsetType      = s.BraceStrutPlacement,
                    FlrGirtHt       = s.FlrGirtHt,
                    HBDivisor       = s.HBDivisor
                };
                var bay = new BayEntry
                {
                    LeftBentIndex     = s.BentNumber,
                    RightBentIndex    = s.BentNumber + 1,
                    BayWidth          = s.BayWidth,
                    GenerateBayMembers = Module1.GenerateBayMembers
                };

                var project = new BentProjectFile();
                project.Bents.Add(bent);
                project.Bays.Add(bay);

                string json = JsonSerializer.Serialize(project, JsonOpts);
                File.WriteAllText(dlg.FileName, json);
                MessageBox.Show("Project saved to:\n" + dlg.FileName, "TSave",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        [CommandMethod("TLoad")]
        public static void TLoad()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Timber Project|*.tproj";
                dlg.Title  = "Load Timber Project";
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                BentProjectFile project;
                try
                {
                    string json = File.ReadAllText(dlg.FileName);
                    project = JsonSerializer.Deserialize<BentProjectFile>(json, JsonOpts);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("Failed to read project file:\n" + ex.Message, "TLoad",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (project?.Bents == null || project.Bents.Count == 0)
                {
                    MessageBox.Show("Project file contains no bent data.", "TLoad",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var bent = project.Bents[0];
                var s    = Properties.Settings.Default;

                s.BentNumber          = bent.Index;
                s.TrussType           = bent.TrussType;
                s.Span                = bent.Span;
                s.EaveHt              = bent.EaveHt;
                s.RoofPitchRise       = bent.PitchRise;
                s.RoofPitchRun        = bent.PitchRun;
                s.WallNumber          = bent.WallNumber;
                s.Make3D              = bent.Make3D;
                s.HasJoinery          = bent.HasJoinery;
                s.DeletePolylines     = bent.DeletePolylines;
                s.BraceStrutPlacement = bent.OffsetType;
                s.FlrGirtHt           = bent.FlrGirtHt;
                s.HBDivisor           = bent.HBDivisor;

                if (project.Bays != null && project.Bays.Count > 0)
                {
                    s.BayWidth                = project.Bays[0].BayWidth;
                    Module1.GenerateBayMembers = project.Bays[0].GenerateBayMembers;
                }

                s.Save();

                // Reopen the TDraw palette so the UI picks up the new Settings values.
                Commands.ps = null;
                Commands.ShowPalette();
            }
        }
    }
}
