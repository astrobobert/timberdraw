using System.Collections.ObjectModel;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw.Browser
{
    // One row in the Frame Browser: a managed timber's identity + display fields.
    public sealed class FrameBrowserItem
    {
        public ObjectId Id;
        public string Type { get; set; }
        public string Designation { get; set; }
        public string Size { get; set; }
        public string Handle { get; set; }
        public double W { get; set; }   // section width  (for the property editor)
        public double D { get; set; }   // section depth
        // Grouping layer tags (the organization spine).
        public string Frame { get; set; } = "";   // frame tag (A, B, ...)
        public string Bent { get; set; } = "";     // Arabic bent number (1, 2, ...) -- bent members
        public string Bay { get; set; } = "";      // Roman bay numeral (I, II, ...) -- longitudinal members
        public string Wall { get; set; } = "";     // explicit wall letter (TAssign); blank -> derived
        public string GridLabel { get; set; } = ""; // structural-grid label (1A / 1BC), stamped at emit

        // Frame group header ("Frame A"); blank tag -> "(ungrouped)".
        public string FrameGroup => string.IsNullOrEmpty(Frame) ? "(ungrouped)" : "Frame " + Frame;

        // A timber with NO grid ownership at all: freshly TPlace'd and not yet TAssign'ed (no frame,
        // bent, bay, or wall). It reads as unassigned rather than being force-fit to a derived wall.
        // An emitted bay member carries Frame/Bay, so this is true only for genuinely free timbers.
        private bool IsUnassigned =>
            string.IsNullOrEmpty(Frame) && string.IsNullOrEmpty(Bent)
            && string.IsNullOrEmpty(Bay) && string.IsNullOrEmpty(Wall);

        // Owner group (the structural grid): a bent member -> "Bent 1"; a longitudinal member ->
        // "Wall A"/"Wall B" (explicit assignment, else derived by side; ridge -> first wall). A free,
        // unassigned timber -> "(unassigned)". Exclusive ownership -> each timber appears once.
        public string OwnerGroup => IsUnassigned ? "(unassigned)" : FrameWalls.Owner(Bent, Wall, Type, Designation);

        // Bay sub-group WITHIN a wall ("a wall is a collection of bays"): wall-owned member -> "Bay I";
        // bent-owned member -> "" so its (collapsed) sub-header disappears and bents read as a flat list.
        public string BaySubGroup =>
            string.IsNullOrEmpty(Bent) && !string.IsNullOrEmpty(Bay) ? "Bay " + Bay : "";

        // The wall letter to show/sort by: explicit assignment if present, else derived from side.
        private string EffWall => FrameWalls.EffectiveWall(Wall, Type, Designation);

        // Grid reference matching the framing convention. Prefer the structural-grid label stamped at
        // emit ("1A" / "1BC"); fall back to the derived bent+side / wall+bay for free/legacy timbers.
        // An unassigned free timber has no grid address yet -> blank.
        public string GridRef =>
            IsUnassigned ? ""
            : !string.IsNullOrEmpty(GridLabel) ? GridLabel
            : !string.IsNullOrEmpty(Bent) ? Bent + FrameWalls.SideLetter(Designation)
            : EffWall + (string.IsNullOrEmpty(Bay) ? "" : "-" + Bay);

        // Sort keys so groups render in grid order: bents (rank 0, numbered) before walls (rank 1,
        // lettered), bays in numeric order within a wall.
        public int OwnerRank => string.IsNullOrEmpty(Bent) ? 1 : 0;
        public int OwnerKey  => string.IsNullOrEmpty(Bent)
            ? (EffWall.Length > 0 ? EffWall[0] - 'A' : 0)
            : (int.TryParse(Bent, out int n) ? n : 0);
        public int BayRank => FrameWalls.RomanToInt(Bay);

        // Trailing number of the grid label (P1/C1 -> 1, 2..) so numbered roof timbers render in order
        // within their bay; 0 when the label has no trailing digits.
        public int GridSeq
        {
            get
            {
                string s = GridLabel ?? "";
                int i = s.Length;
                while (i > 0 && char.IsDigit(s[i - 1])) i--;
                return i < s.Length && int.TryParse(s.Substring(i), out int n) ? n : 0;
            }
        }
    }

    // Phase 3 WPF pilot view-model: a bound list of the active drawing's managed timbers, a type/
    // handle filter (also the keyboard-focus spike), a node count, Refresh, and select-to-zoom
    // (setting SelectedItem zooms + grip-selects that timber in the drawing). All AutoCAD interop is
    // delegated to ManagedView so this stays pure presentation.
    public sealed class FrameBrowserViewModel : ObservableBase
    {
        private readonly System.Collections.Generic.List<FrameBrowserItem> _all = new();

        public ObservableCollection<FrameBrowserItem> Timbers { get; } = new();

        public FrameBrowserViewModel()
        {
            RefreshCommand = new RelayCommand(Refresh);
            ApplyCommand = new RelayCommand(Apply, () => _selected != null);

            // Group the list Frame -> Owner -> Bay (the structural grid): the default view over Timbers
            // gets three PropertyGroupDescriptions. Bents are owners with empty Bay sub-groups (the XAML
            // collapses those headers -> flat); walls nest by bay. Grouping persists across filter
            // repopulation since it lives on the view, not the rows; ApplyFilter orders the rows so the
            // groups render in grid order.
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Timbers);
            if (view != null && view.GroupDescriptions != null)
            {
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("FrameGroup"));
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("OwnerGroup"));
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("BaySubGroup"));
            }

            Refresh();
        }

        public ICommand RefreshCommand { get; }
        public RelayCommand ApplyCommand { get; }

        private FrameBrowserItem _selected;
        public FrameBrowserItem SelectedItem
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                if (_selected != null)
                {
                    // Populate the property editor and zoom/grip-select the timber.
                    EditType = _selected.Type;
                    EditW = _selected.W.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    EditD = _selected.D.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ManagedView.ZoomAndHighlight(_selected.Id);
                }
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }

        // ---- property editor (write-back) ----
        private string _editType = "";
        public string EditType { get => _editType; set => Set(ref _editType, value); }

        private string _editW = "";
        public string EditW { get => _editW; set => Set(ref _editW, value); }

        private string _editD = "";
        public string EditD { get => _editD; set => Set(ref _editD, value); }

        // Re-section the selected timber and reselect the regenerated solid (new handle).
        private void Apply()
        {
            if (_selected == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(_editW, System.Globalization.NumberStyles.Any, ci, out double w) || w <= 0) return;
            if (!double.TryParse(_editD, System.Globalization.NumberStyles.Any, ci, out double d) || d <= 0) return;

            ObjectId newId = ManagedView.ApplySection(_selected.Id, w, d, (_editType ?? "").Trim());
            Refresh();
            if (!newId.IsNull)
                foreach (FrameBrowserItem it in Timbers)
                    if (it.Id == newId) { SelectedItem = it; break; }
        }

        private string _filter = "";
        public string FilterText
        {
            get => _filter;
            set { if (Set(ref _filter, value)) ApplyFilter(); }
        }

        private string _status = "";
        public string Status { get => _status; private set => Set(ref _status, value); }

        // Reload the rows from the active drawing (managed timbers + node count).
        public void Refresh()
        {
            _all.Clear();
            _all.AddRange(ManagedView.LoadTimbers());
            int nodes = ManagedView.NodeCount();
            Status = _all.Count + " timber(s), " + nodes + " node(s)";
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string f = (_filter ?? "").Trim();
            Timbers.Clear();
            // Order by the grid (Frame -> bents-then-walls -> bay) so WPF renders the groups in that
            // order (group display order follows collection order). Numeric/roman ranks avoid wrong
            // alpha ordering (e.g. bay "IX" before "V", bent "10" before "2").
            var rows = new System.Collections.Generic.List<FrameBrowserItem>(_all);
            rows.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.FrameGroup, b.FrameGroup); if (c != 0) return c;
                c = a.OwnerRank.CompareTo(b.OwnerRank);                    if (c != 0) return c;
                c = a.OwnerKey.CompareTo(b.OwnerKey);                      if (c != 0) return c;
                c = a.BayRank.CompareTo(b.BayRank);                        if (c != 0) return c;
                return a.GridSeq.CompareTo(b.GridSeq);
            });
            foreach (FrameBrowserItem it in rows)
            {
                // STARTS-WITH (not substring) so "Post" matches Post but not KingPost; "King" -> KingPost.
                if (f.Length == 0
                    || (it.Type != null && it.Type.StartsWith(f, System.StringComparison.OrdinalIgnoreCase))
                    || (it.Handle != null && it.Handle.StartsWith(f, System.StringComparison.OrdinalIgnoreCase)))
                    Timbers.Add(it);
            }
        }
    }
}
