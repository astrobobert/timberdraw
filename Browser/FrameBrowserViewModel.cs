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
        public string Floor { get; set; } = "";    // floor level, digits bottom-up -- joists/summers
        public string GridLabel { get; set; } = ""; // structural-grid label (1A / 1BC), stamped at emit

        // Frame group header ("Frame A"); blank tag -> "(ungrouped)".
        public string FrameGroup => string.IsNullOrEmpty(Frame) ? "(ungrouped)" : "Frame " + Frame;

        // A timber with NO grid ownership at all: freshly TPlace'd and not yet TAssign'ed (no frame,
        // bent, bay, wall, or floor). It reads as unassigned rather than being force-fit to a derived
        // wall. An emitted bay member carries Frame/Bay, so this is true only for genuinely free timbers.
        private bool IsUnassigned =>
            string.IsNullOrEmpty(Frame) && string.IsNullOrEmpty(Bent)
            && string.IsNullOrEmpty(Bay) && string.IsNullOrEmpty(Wall) && string.IsNullOrEmpty(Floor);

        // Owner group (the organization hierarchy): a bent member -> "Bent 1"; a floor-system member
        // -> "Floor 1"; a longitudinal member -> "Wall A"/"Wall B" (explicit assignment, else derived
        // by side; ridge -> first wall). A free, unassigned timber -> "(unassigned)". Exclusive
        // ownership -> each timber appears once.
        public string OwnerGroup =>
            IsUnassigned ? "(unassigned)"
            : !string.IsNullOrEmpty(Floor) && string.IsNullOrEmpty(Bent) && string.IsNullOrEmpty(Wall)
                ? "Floor " + Floor
                : FrameWalls.Owner(Bent, Wall, Type, Designation);

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
        // lettered) before floors (rank 2, level-numbered), bays in numeric order within a wall.
        private bool IsFloorOwned => !string.IsNullOrEmpty(Floor)
            && string.IsNullOrEmpty(Bent) && string.IsNullOrEmpty(Wall);
        public int OwnerRank => !string.IsNullOrEmpty(Bent) ? 0 : IsFloorOwned ? 2 : 1;
        public int OwnerKey =>
            !string.IsNullOrEmpty(Bent) ? (int.TryParse(Bent, out int n) ? n : 0)
            : IsFloorOwned ? (int.TryParse(Floor, out int fl) ? fl : 0)
            : (EffWall.Length > 0 ? EffWall[0] - 'A' : 0);
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
            // ONE Apply (Robert's call; Assign folded in): commits whatever the user edited.
            ApplyCommand = new RelayCommand(Apply, () => SelectedCount > 0);

            // Refresh whenever a TAssign completes (fired from here or anywhere else) so new
            // labels/groups appear without a manual Refresh. Runs on the AutoCAD UI thread.
            ManagedAssembly.Applied += Refresh;

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

        // What the user actually EDITED since the last load/apply -- Apply commits only these
        // (review-loading a row populates the same fields, so loads must never read as edits).
        private bool _loadingSel;
        private bool _sectionDirty;
        private bool _assignDirty;

        private FrameBrowserItem _selected;
        public FrameBrowserItem SelectedItem
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                if (_selected != null)
                {
                    _loadingSel = true;
                    // Populate the property editor (zoom happens in SetSelectedMany, single-pick only).
                    EditType = _selected.Type;
                    EditW = _selected.W.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    EditD = _selected.D.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    // REVIEW: show the picked timber's CURRENT assembly in the (editable) fields.
                    // An unassigned timber leaves the fields as they are, ready to assign.
                    if (!string.IsNullOrEmpty(_selected.Frame)) AsmFrame = _selected.Frame;
                    if (!string.IsNullOrEmpty(_selected.Bent))
                    { AsmKind = "Bent"; AsmOwner = _selected.Bent; AsmBay = ColumnOf(_selected); }
                    else if (!string.IsNullOrEmpty(_selected.Wall))
                    { AsmKind = "Wall"; AsmOwner = _selected.Wall; AsmBay = _selected.Bay; }
                    else if (!string.IsNullOrEmpty(_selected.Floor))
                    { AsmKind = "Floor"; AsmOwner = _selected.Floor; AsmBay = ""; }
                    _loadingSel = false;
                    _sectionDirty = false;   // the section fields now mirror the row
                }
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }

        // Full multi-selection from the ListView (SelectedItems isn't bindable -- the code-behind
        // pushes it on SelectionChanged). Every selection change grip-HIGHLIGHTS the timbers in the
        // drawing; the view never moves on select -- zoom is on demand (double-click, ZoomSelected).
        private readonly System.Collections.Generic.List<FrameBrowserItem> _selectedMany = new();
        private int SelectedCount => _selectedMany.Count > 0 ? _selectedMany.Count : (_selected != null ? 1 : 0);
        public void SetSelectedMany(System.Collections.IList items)
        {
            _selectedMany.Clear();
            if (items != null)
                foreach (object o in items)
                    if (o is FrameBrowserItem it) _selectedMany.Add(it);
            var ids = new System.Collections.Generic.List<ObjectId>(_selectedMany.Count);
            foreach (FrameBrowserItem it in _selectedMany) ids.Add(it.Id);
            ManagedView.Highlight(ids);
            ApplyCommand.RaiseCanExecuteChanged();
        }

        // Double-click zoom: frame the (single) clicked row's timber in the view.
        public void ZoomSelected()
        {
            if (_selected != null) ManagedView.ZoomAndHighlight(_selected.Id);
        }

        // ---- assembly (assign the selected rows: Frame -> Bent / Wall+Bay / Floor). This is
        // THE assign surface -- the Assembly tab carries no assign controls. ----
        public System.Collections.Generic.List<string> AsmKinds { get; } = new() { "Bent", "Wall", "Floor" };

        private string _asmFrame = "A";
        public string AsmFrame
        {
            get => _asmFrame;
            set { if (Set(ref _asmFrame, value) && !_loadingSel) _assignDirty = true; }
        }

        private string _asmKind = "Bent";
        public string AsmKind
        {
            get => _asmKind;
            set
            {
                if (!Set(ref _asmKind, value)) return;
                if (!_loadingSel) _assignDirty = true;
                Raise(nameof(AsmExtraEnabled));
                Raise(nameof(AsmExtraLabel));
            }
        }
        // The second grid coordinate: a Bent owner takes a COLUMN letter (intersection 2C), a Wall a
        // Bay; a Floor is one number alone.
        public bool AsmExtraEnabled => _asmKind != "Floor";
        public string AsmExtraLabel => _asmKind == "Wall" ? "Bay" : "Col";

        // Trailing 1-2 letter column of a bent timber's grid label ("2C" -> "C"); "" when none.
        private static string ColumnOf(FrameBrowserItem it)
        {
            string s = it.GridLabel ?? "";
            int i = s.Length;
            while (i > 0 && char.IsLetter(s[i - 1])) i--;
            string tail = s.Substring(i);
            return i > 0 && tail.Length >= 1 && tail.Length <= 2 ? tail : "";
        }

        private string _asmOwner = "1";
        public string AsmOwner
        {
            get => _asmOwner;
            set { if (Set(ref _asmOwner, value) && !_loadingSel) _assignDirty = true; }
        }

        private string _asmBay = "";
        public string AsmBay
        {
            get => _asmBay;
            set { if (Set(ref _asmBay, value) && !_loadingSel) _assignDirty = true; }
        }

        // Hand the selected rows + target to TAssign (via ManagedView.Assign -> ManagedAssembly
        // stash -> command context). The Applied event refreshes the rows when it completes.
        private void AssignSelected()
        {
            var ids = new System.Collections.Generic.List<ObjectId>();
            if (_selectedMany.Count > 0) foreach (FrameBrowserItem it in _selectedMany) ids.Add(it.Id);
            else if (_selected != null) ids.Add(_selected.Id);
            if (ids.Count == 0) return;
            ManagedView.Assign(ids, (_asmFrame ?? "").Trim(), _asmKind, (_asmOwner ?? "").Trim(),
                               AsmExtraEnabled ? (_asmBay ?? "").Trim() : "");
        }

        // ---- property editor (write-back) ----
        private string _editType = "";
        public string EditType
        {
            get => _editType;
            set { if (Set(ref _editType, value) && !_loadingSel) _sectionDirty = true; }
        }

        private string _editW = "";
        public string EditW
        {
            get => _editW;
            set { if (Set(ref _editW, value) && !_loadingSel) _sectionDirty = true; }
        }

        private string _editD = "";
        public string EditD
        {
            get => _editD;
            set { if (Set(ref _editD, value) && !_loadingSel) _sectionDirty = true; }
        }

        // The ONE commit button: re-section if the section fields were edited, assign if the
        // address fields were edited -- either, or both. (If both, the assign runs through the
        // deferred TAssign command and skips any handle the re-section just replaced; re-Apply
        // covers that rare double.)
        private void Apply()
        {
            if (_sectionDirty && _selected != null)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (double.TryParse(_editW, System.Globalization.NumberStyles.Any, ci, out double w) && w > 0
                    && double.TryParse(_editD, System.Globalization.NumberStyles.Any, ci, out double d) && d > 0)
                {
                    ObjectId newId = ManagedView.ApplySection(_selected.Id, w, d, (_editType ?? "").Trim());
                    _sectionDirty = false;
                    Refresh();
                    if (!newId.IsNull)
                        foreach (FrameBrowserItem it in Timbers)
                            if (it.Id == newId) { SelectedItem = it; break; }
                }
            }
            if (_assignDirty && !string.IsNullOrWhiteSpace(_asmOwner))
            {
                AssignSelected();
                _assignDirty = false;
            }
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
                // Within a group: alphabetical by type, then grid order within a type.
                c = string.CompareOrdinal(a.Type ?? "", b.Type ?? "");     if (c != 0) return c;
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
