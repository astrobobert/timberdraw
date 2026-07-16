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
        // Public: the pulldown choice lists harvest wall letters from it.
        public string EffWall => FrameWalls.EffectiveWall(Wall, Type, Designation);

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
        private bool _typeDirty;
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
                    // Populate the label editor (zoom happens in SetSelectedMany, single-pick only).
                    // SIZE deliberately absent: the browser manipulates the LABEL only -- W x D live
                    // on the Assembly + Frame tabs (Robert's call, 2026-07-16).
                    EditType = _selected.Type;

                    // REVIEW: show the picked timber's CURRENT assembly in the (editable) fields.
                    // An unassigned timber leaves the fields as they are, ready to assign.
                    if (!string.IsNullOrEmpty(_selected.Frame)) AsmFrame = _selected.Frame;
                    if (!string.IsNullOrEmpty(_selected.Bent))
                    { AsmKind = "Bent"; AsmOwner = _selected.Bent; AsmBay = ColumnOf(_selected); }
                    else if (!string.IsNullOrEmpty(_selected.Wall))
                    { AsmKind = "Wall"; AsmOwner = _selected.Wall; AsmBay = _selected.Bay; }
                    else if (!string.IsNullOrEmpty(_selected.Floor))
                    { AsmKind = "Floor"; AsmOwner = _selected.Floor; AsmBay = _selected.Bay; }
                    _loadingSel = false;
                    _typeDirty = false;   // the Type field now mirrors the row
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

        // ONE FRAME PER DRAWING (Robert's call, 2026-07-16): the Frame row is HIDDEN for normal
        // drawings -- the assign target is simply the drawing's frame, tracked here. A LEGACY
        // multi-frame drawing still shows the row as a PICK-ONLY dropdown of its existing tags;
        // new frames are never minted from the browser (the Frame tab owns the frame's lifecycle).
        public ObservableCollection<string> AsmFrames { get; } = new();

        private string _asmFrame = "A";
        public string AsmFrame
        {
            get => _asmFrame;
            set { if (Set(ref _asmFrame, value) && !_loadingSel) _assignDirty = true; }
        }

        public System.Windows.Visibility FrameRowVisibility =>
            AsmFrames.Count > 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        // ---- pulldown choices (Robert's call, 2026-07-16): every field offers what EXISTS.
        // Type is PICK-ONLY (new type names come from the Assembly tab's Sections catalog);
        // the owner + second-coordinate rows stay TYPABLE on top of their lists, because a
        // first member's address doesn't exist anywhere until the assignment mints it. ----
        public ObservableCollection<string> TypeChoices { get; } = new();
        public ObservableCollection<string> OwnerChoices { get; } = new();
        public ObservableCollection<string> ExtraChoices { get; } = new();

        // Per-kind pools harvested from the rows; OwnerChoices/ExtraChoices re-slice on an
        // Assign-to change without re-scanning.
        private readonly System.Collections.Generic.List<string> _bentChoices = new();
        private readonly System.Collections.Generic.List<string> _wallChoices = new();
        private readonly System.Collections.Generic.List<string> _floorChoices = new();
        private readonly System.Collections.Generic.List<string> _bayChoices = new();

        // Rebuild every choice list from the loaded rows + the Sections catalog. Runs with each
        // Refresh, inside _loadingSel -- repopulating an ItemsSource bounces WPF selections, and
        // that must never read as a user edit.
        private void RefreshChoices()
        {
            _loadingSel = true;
            try
            {
                var cmp = System.StringComparer.OrdinalIgnoreCase;

                // Frames: one per drawing; more than one = a legacy drawing, and the row appears.
                var frames = new System.Collections.Generic.SortedSet<string>(cmp);
                foreach (FrameBrowserItem it in _all)
                    if (!string.IsNullOrEmpty(it.Frame)) frames.Add(it.Frame);
                AsmFrames.Clear();
                foreach (string tag in frames) AsmFrames.Add(tag);
                if (!frames.Contains((_asmFrame ?? "").Trim()))
                    AsmFrame = frames.Count > 0 ? frames.Min : "A";
                Raise(nameof(FrameRowVisibility));

                // Types: the Sections catalog (the only mint) + what the drawing actually contains
                // (skeleton roles like KingPost/Common aren't catalog entries but must stay
                // re-typeable).
                var types = new System.Collections.Generic.SortedSet<string>(cmp);
                foreach (string tok in (TimberDraw.Properties.Settings.Default.TimberSections ?? "").Split(','))
                {
                    string t = tok.Split(':')[0].Trim();
                    if (t.Length > 0) types.Add(t);
                }
                foreach (FrameBrowserItem it in _all)
                    if (!string.IsNullOrEmpty(it.Type)) types.Add(it.Type);
                string keepType = _editType;
                TypeChoices.Clear();
                foreach (string t in types) TypeChoices.Add(t);
                EditType = types.Contains((keepType ?? "").Trim()) ? keepType : "";

                // Owner/second-coordinate pools: bents + floors numeric, walls alpha, bays Roman.
                var bents = new System.Collections.Generic.SortedSet<int>();
                var floors = new System.Collections.Generic.SortedSet<int>();
                var walls = new System.Collections.Generic.SortedSet<string>(cmp);
                var bays = new System.Collections.Generic.SortedSet<string>(cmp);
                foreach (FrameBrowserItem it in _all)
                {
                    if (int.TryParse(it.Bent, out int b)) bents.Add(b);
                    if (int.TryParse(it.Floor, out int fl)) floors.Add(fl);
                    if (!string.IsNullOrEmpty(it.EffWall)) walls.Add(it.EffWall);
                    if (!string.IsNullOrEmpty(it.Bay)) bays.Add(it.Bay);
                }
                _bentChoices.Clear(); foreach (int b in bents) _bentChoices.Add(b.ToString());
                _floorChoices.Clear(); foreach (int fl in floors) _floorChoices.Add(fl.ToString());
                _wallChoices.Clear(); _wallChoices.AddRange(walls);
                _bayChoices.Clear(); _bayChoices.AddRange(bays);
                _bayChoices.Sort((a, b2) => FrameWalls.RomanToInt(a).CompareTo(FrameWalls.RomanToInt(b2)));
                RebuildKindChoices();
            }
            finally { _loadingSel = false; }
        }

        // Slice the per-kind pools into the two visible lists (also on an Assign-to change).
        private void RebuildKindChoices()
        {
            OwnerChoices.Clear();
            foreach (string s in _asmKind == "Wall" ? _wallChoices
                               : _asmKind == "Floor" ? _floorChoices : _bentChoices)
                OwnerChoices.Add(s);
            ExtraChoices.Clear();
            foreach (string s in _asmKind == "Bent" ? _wallChoices : _bayChoices)
                ExtraChoices.Add(s);
        }

        private string _asmKind = "Bent";
        public string AsmKind
        {
            get => _asmKind;
            set
            {
                if (!Set(ref _asmKind, value)) return;
                if (!_loadingSel) _assignDirty = true;
                Raise(nameof(AsmOwnerLabel));
                Raise(nameof(AsmExtraLabel));
                RebuildKindChoices();
            }
        }
        // The address rows NAME THEMSELVES from the Assign-to choice ('Kind'/'Owner'/'Col' read as
        // jargon -- Robert's call, 2026-07-16): the owner row says which coordinate you type (Bent
        // number / Wall letter / Floor level), and the second row is the OTHER grid coordinate --
        // a Bent owner takes the intersection's wall letter (the C in 2C), a Wall its bay numeral,
        // and a Floor its bay too (floor members carry a bay designation now).
        public string AsmOwnerLabel => _asmKind == "Wall" ? "Wall" : _asmKind == "Floor" ? "Floor" : "Bent";
        public string AsmExtraLabel => _asmKind == "Bent" ? "Wall" : "Bay";

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
                               (_asmBay ?? "").Trim());
        }

        // ---- label editor (write-back; the browser never touches W x D) ----
        private string _editType = "";
        public string EditType
        {
            get => _editType;
            set { if (Set(ref _editType, value) && !_loadingSel) _typeDirty = true; }
        }

        // The ONE commit button: re-type if the Type field was edited (the timber's CURRENT section
        // rides along unchanged -- sizing belongs to the Assembly + Frame tabs); assign whenever the
        // address fields point somewhere the selection isn't already (NOT just when they were edited
        // this time -- the dirty-only gate made the SECOND batch a silent no-op, and reviewing a row
        // that loaded the right target then selecting fresh timbers did nothing on Apply).
        private void Apply()
        {
            string type = (_editType ?? "").Trim();
            if (_typeDirty && _selected != null && type.Length > 0)
            {
                ObjectId newId = ManagedView.ApplySection(_selected.Id, _selected.W, _selected.D, type);
                _typeDirty = false;
                Refresh();
                if (!newId.IsNull)
                    foreach (FrameBrowserItem it in Timbers)
                        if (it.Id == newId) { SelectedItem = it; break; }
            }
            if (!string.IsNullOrWhiteSpace(_asmOwner) && (_assignDirty || TargetDiffers()))
            {
                AssignSelected();
                _assignDirty = false;
            }
        }

        // TRUE when any selected row's current assignment differs from the target fields -- the
        // fields are WYSIWYG: whatever they show is where Apply puts the selection. A single row
        // loaded for review shows its own address, so a section-only Apply stays a section-only
        // Apply; fresh (unassigned) timbers always differ.
        private bool TargetDiffers()
        {
            var sel = _selectedMany.Count > 0
                ? (System.Collections.Generic.IEnumerable<FrameBrowserItem>)_selectedMany
                : _selected != null ? new[] { _selected } : null;
            if (sel == null) return false;
            var cmp = System.StringComparer.OrdinalIgnoreCase;
            string frame = string.IsNullOrWhiteSpace(_asmFrame) ? "A" : _asmFrame.Trim();
            string owner = (_asmOwner ?? "").Trim();
            string bay = (_asmBay ?? "").Trim();
            foreach (FrameBrowserItem it in sel)
            {
                if (!cmp.Equals(it.Frame ?? "", frame)) return true;
                switch (_asmKind)
                {
                    case "Bent":
                        // The Wall box only refines the minted label; ownership is the bent number.
                        if (!cmp.Equals(it.Bent ?? "", StripIntersection(owner))) return true; break;
                    case "Wall":
                        if (!cmp.Equals(it.Wall ?? "", owner) || !cmp.Equals(it.Bay ?? "", bay)) return true; break;
                    case "Floor":
                        if (!cmp.Equals(it.Floor ?? "", owner) || !cmp.Equals(it.Bay ?? "", bay)) return true; break;
                }
            }
            return false;
        }

        // The leading digits of a possibly combined "2C" bent owner (matches TAssign's split).
        private static string StripIntersection(string owner)
        {
            int i = 0;
            while (i < owner.Length && char.IsDigit(owner[i])) i++;
            return owner.Substring(0, i).Length > 0 ? owner.Substring(0, i) : owner;
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
            RefreshChoices();
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
