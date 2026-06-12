using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AsciiDraw.Models;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsciiDraw.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public DrawDocument Document { get; private set; } = new();
        public HashSet<Guid> Selection { get; } = new();
        public ObservableCollection<LayerItem> Layers { get; } = new();

        /// <summary>Raised when element geometry/content changed and the canvas must repaint.</summary>
        public event Action? DocumentChanged;
        /// <summary>Raised when the set of selected elements changed.</summary>
        public event Action? SelectionChanged;
        /// <summary>Raised after the Layers collection was rebuilt.</summary>
        public event Action? LayersRebuilt;

        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();
        private string? _lastUndoTag;
        private string? _filePath;

        [ObservableProperty]
        private string _windowTitle = "unnamed.asciidraw - AsciiDraw";

        [ObservableProperty]
        private string _selectionStatus = "No selection";

        [ObservableProperty]
        private string _cursorStatus = "";

        public string[] ZoomItems { get; } = { "50%", "75%", "100%", "125%", "150%", "200%" };

        [ObservableProperty]
        private string _zoomText = "100%";

        public double ZoomFactor =>
            double.TryParse(ZoomText.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)
                ? p / 100.0
                : 1.0;

        // ----- Tools -----

        private Tool _currentTool = Tool.Select;
        public Tool CurrentTool
        {
            get => _currentTool;
            set
            {
                if (_currentTool != value)
                {
                    _currentTool = value;
                    OnPropertyChanged();
                }
                OnPropertyChanged(nameof(IsSelectTool));
                OnPropertyChanged(nameof(IsRectTool));
                OnPropertyChanged(nameof(IsTextTool));
                OnPropertyChanged(nameof(IsLineTool));
            }
        }

        public bool IsSelectTool
        {
            get => CurrentTool == Tool.Select;
            set { if (value) CurrentTool = Tool.Select; else CurrentTool = CurrentTool; }
        }

        public bool IsRectTool
        {
            get => CurrentTool == Tool.Rect;
            set { if (value) CurrentTool = Tool.Rect; else CurrentTool = CurrentTool; }
        }

        public bool IsTextTool
        {
            get => CurrentTool == Tool.Text;
            set { if (value) CurrentTool = Tool.Text; else CurrentTool = CurrentTool; }
        }

        public bool IsLineTool
        {
            get => CurrentTool == Tool.Line;
            set { if (value) CurrentTool = Tool.Line; else CurrentTool = CurrentTool; }
        }

        // ----- Selection -----

        public IEnumerable<DrawElement> SelectedElements =>
            Document.Elements.Where(e => Selection.Contains(e.Id));

        public DrawElement? SingleSelected =>
            Selection.Count == 1 ? Document.Elements.FirstOrDefault(e => e.Id == Selection.First()) : null;

        public RectElement? SingleRect => SingleSelected as RectElement;
        public LineElement? SingleLine => SingleSelected as LineElement;

        public bool IsRectSelected => SingleRect != null;
        public bool IsLineSelected => SingleLine != null;
        public bool IsGroupSelected => SingleSelected == null && SelectedGroup != null;
        public bool ShowPlaceholder => SingleSelected == null && SelectedGroup == null;

        /// <summary>The deepest group whose subtree is exactly the current selection,
        /// i.e. what the user gets by clicking a group (header or on canvas).</summary>
        public GroupInfo? SelectedGroup
        {
            get
            {
                if (Selection.Count == 0)
                    return null;
                GroupInfo? best = null;
                int bestDepth = -1;
                foreach (var g in Document.Groups)
                {
                    var ids = SubtreeElements(g.Id).Select(e => e.Id).ToList();
                    if (ids.Count != Selection.Count || !ids.All(Selection.Contains))
                        continue;
                    int depth = AncestorGroups(g.Id).Count();
                    if (depth > bestDepth)
                    {
                        best = g;
                        bestDepth = depth;
                    }
                }
                return best;
            }
        }

        public string SelectionHeader => Selection.Count switch
        {
            0 => "No Selection",
            1 => SingleSelected?.Name ?? "",
            _ => $"{Selection.Count} elements selected",
        };

        public void SetSelection(IEnumerable<Guid> ids)
        {
            Selection.Clear();
            foreach (var id in ids)
                Selection.Add(id);
            NotifySelectionChanged();
        }

        public void ToggleSelection(IEnumerable<Guid> ids)
        {
            var list = ids.ToList();
            if (list.All(Selection.Contains))
                foreach (var id in list) Selection.Remove(id);
            else
                foreach (var id in list) Selection.Add(id);
            NotifySelectionChanged();
        }

        public void ClearSelection()
        {
            if (Selection.Count == 0)
                return;
            Selection.Clear();
            NotifySelectionChanged();
        }

        // ----- Group tree helpers -----
        // Groups form a tree: GroupInfo.ParentId points at the parent group, and
        // elements carry the id of their *immediate* group.

        private GroupInfo? FindGroup(Guid id) => Document.Groups.FirstOrDefault(g => g.Id == id);

        /// <summary>Group chain from the immediate group up to the root.</summary>
        private IEnumerable<Guid> AncestorGroups(Guid? groupId)
        {
            var seen = new HashSet<Guid>();
            while (groupId is Guid g && seen.Add(g))
            {
                yield return g;
                groupId = FindGroup(g)?.ParentId;
            }
        }

        private Guid? RootGroupOf(DrawElement el)
        {
            Guid? root = null;
            foreach (var g in AncestorGroups(el.GroupId))
                root = g;
            return root;
        }

        private bool IsInSubtree(Guid? groupId, Guid ancestor) =>
            AncestorGroups(groupId).Contains(ancestor);

        /// <summary>All elements inside a group, including nested groups, in z-order.</summary>
        public IEnumerable<DrawElement> SubtreeElements(Guid groupId) =>
            Document.Elements.Where(e => IsInSubtree(e.GroupId, groupId));

        public bool IsGroupFullySelected(Guid groupId)
        {
            bool any = false;
            foreach (var el in SubtreeElements(groupId))
            {
                any = true;
                if (!Selection.Contains(el.Id))
                    return false;
            }
            return any;
        }

        /// <summary>Returns the ids that should be selected when this element is clicked
        /// (the whole root group when the element belongs to one).</summary>
        public IReadOnlyList<Guid> ExpandToGroup(DrawElement el) =>
            RootGroupOf(el) is Guid root
                ? SubtreeElements(root).Select(e => e.Id).ToList()
                : new List<Guid> { el.Id };

        public void SelectFromLayers(IEnumerable<LayerItem> items)
        {
            var ids = new HashSet<Guid>();
            foreach (var li in items)
            {
                if (li.IsGroup)
                    foreach (var el in SubtreeElements(li.Id))
                        ids.Add(el.Id);
                else
                    ids.Add(li.Id);
            }
            SetSelection(ids);
        }

        public void NotifySelectionChanged()
        {
            UpdateSelectionStatus();
            RaiseProxyChanges();
            SelectionChanged?.Invoke();
        }

        private void UpdateSelectionStatus()
        {
            SelectionStatus = Selection.Count switch
            {
                0 => "No selection",
                1 => Describe(SingleSelected!),
                _ => $"Selection: {Selection.Count} elements",
            };
        }

        private static string Describe(DrawElement el) => el switch
        {
            RectElement r => $"Selection: {r.Name}: {r.Width}x{r.Height} at ({r.X}, {r.Y})",
            LineElement l => $"Selection: {l.Name}: ({l.X1}, {l.Y1}) → ({l.X2}, {l.Y2})",
            _ => "",
        };

        private void RaiseProxyChanges()
        {
            OnPropertyChanged(nameof(IsRectSelected));
            OnPropertyChanged(nameof(IsLineSelected));
            OnPropertyChanged(nameof(IsGroupSelected));
            OnPropertyChanged(nameof(ShowPlaceholder));
            OnPropertyChanged(nameof(SelectionHeader));
            OnPropertyChanged(nameof(SelGroupName));
            OnPropertyChanged(nameof(SelName));
            OnPropertyChanged(nameof(SelBorderStyle));
            OnPropertyChanged(nameof(SelFill));
            OnPropertyChanged(nameof(SelVAlign));
            OnPropertyChanged(nameof(SelHAlign));
            OnPropertyChanged(nameof(SelText));
            OnPropertyChanged(nameof(SelLineStyle));
            OnPropertyChanged(nameof(SelStartArrow));
            OnPropertyChanged(nameof(SelEndArrow));
        }

        // ----- Hit testing -----

        public DrawElement? HitTest(int cx, int cy)
        {
            for (int i = Document.Elements.Count - 1; i >= 0; i--)
            {
                var el = Document.Elements[i];
                if (Hit(el, cx, cy))
                    return el;
            }
            return null;
        }

        private static bool Hit(DrawElement el, int cx, int cy)
        {
            switch (el)
            {
                case RectElement r:
                    if (!r.Contains(cx, cy))
                        return false;
                    bool onBorder = cx == r.X || cx == r.X + r.Width - 1 || cy == r.Y || cy == r.Y + r.Height - 1;
                    if (r.LineStyle != LineStyle.None && onBorder)
                        return true;
                    if (r.FillStyle == FillStyle.Solid)
                        return true;
                    if (r.IsTextBox)
                        return true;
                    return !string.IsNullOrEmpty(r.Text);
                case LineElement l:
                    return l.RouteCells().Contains((cx, cy));
                default:
                    return false;
            }
        }

        // ----- Mutation helpers -----

        public void PushUndo(string? tag = null)
        {
            if (tag != null && tag == _lastUndoTag)
                return;
            _lastUndoTag = tag;
            _undoStack.Push(Document.ToJson());
            _redoStack.Clear();
        }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (_isDirty == value)
                    return;
                _isDirty = value;
                UpdateTitle();
            }
        }

        public void NotifyDocumentChanged()
        {
            UpdateSelectionStatus();
            IsDirty = true;
            DocumentChanged?.Invoke();
        }

        public void NotifyStructureChanged()
        {
            RebuildLayers();
            NotifyDocumentChanged();
        }

        public void AddElement(DrawElement el, bool isTextBox = false)
        {
            el.Name = DefaultName(el, isTextBox);
            Document.Elements.Add(el);
            NotifyStructureChanged();
        }

        private string DefaultName(DrawElement el, bool isTextBox)
        {
            return el switch
            {
                RectElement when isTextBox =>
                    $"Text {Document.Elements.OfType<RectElement>().Count(r => r.IsTextBox) + 1}",
                RectElement =>
                    $"Rect {Document.Elements.OfType<RectElement>().Count(r => !r.IsTextBox) + 1}",
                LineElement =>
                    $"Line {Document.Elements.OfType<LineElement>().Count() + 1}",
                _ => "Element",
            };
        }

        public void TranslateSelected(int dx, int dy)
        {
            foreach (var el in SelectedElements)
                el.Translate(dx, dy);
            SyncLinkedLines();
            NotifyDocumentChanged();
        }

        /// <summary>Re-derives endpoints of linked lines from their anchor points.</summary>
        public void SyncLinkedLines()
        {
            foreach (var l in Document.Elements.OfType<LineElement>())
            {
                if (l.StartLink is Guid s)
                {
                    var r = FindRect(s);
                    if (r == null)
                        l.StartLink = null;
                    else
                        (l.X1, l.Y1) = r.ConnectionCell(l.StartAnchor);
                }
                if (l.EndLink is Guid t)
                {
                    var r = FindRect(t);
                    if (r == null)
                        l.EndLink = null;
                    else
                        (l.X2, l.Y2) = r.ConnectionCell(l.EndAnchor);
                }
            }
        }

        private RectElement? FindRect(Guid id) =>
            Document.Elements.OfType<RectElement>().FirstOrDefault(r => r.Id == id);

        /// <summary>Called after a rect was resized or a line endpoint moved manually.</summary>
        public void GeometryChanged()
        {
            SyncLinkedLines();
            NotifyDocumentChanged();
        }

        /// <summary>Topmost rectangle whose bounds (inflated by one cell) contain the cell,
        /// together with its nearest connection point.</summary>
        public (RectElement Rect, Anchor Anchor)? FindSnapTarget(int cx, int cy)
        {
            for (int i = Document.Elements.Count - 1; i >= 0; i--)
            {
                if (Document.Elements[i] is not RectElement r)
                    continue;
                if (cx < r.X - 1 || cx > r.X + r.Width || cy < r.Y - 1 || cy > r.Y + r.Height)
                    continue;
                Anchor best = Anchor.TopLeft;
                long bestDist = long.MaxValue;
                foreach (Anchor a in Enum.GetValues<Anchor>())
                {
                    var (ax, ay) = r.AnchorCell(a);
                    long d = (long)(ax - cx) * (ax - cx) + (long)(ay - cy) * (ay - cy);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = a;
                    }
                }
                return (r, best);
            }
            return null;
        }

        /// <summary>Moves one endpoint of a line to the given cell, snapping and linking it
        /// to the nearest connection point when a rectangle is close. Returns the snap target
        /// (for highlighting) or null when the endpoint is free.</summary>
        public (RectElement Rect, Anchor Anchor)? SnapLineEndpoint(LineElement l, bool start, int cx, int cy)
        {
            var snap = FindSnapTarget(cx, cy);
            if (start)
            {
                l.StartLink = snap?.Rect.Id;
                if (snap.HasValue)
                {
                    l.StartAnchor = snap.Value.Anchor;
                    (l.X1, l.Y1) = snap.Value.Rect.ConnectionCell(snap.Value.Anchor);
                }
                else
                {
                    (l.X1, l.Y1) = (cx, cy);
                }
            }
            else
            {
                l.EndLink = snap?.Rect.Id;
                if (snap.HasValue)
                {
                    l.EndAnchor = snap.Value.Anchor;
                    (l.X2, l.Y2) = snap.Value.Rect.ConnectionCell(snap.Value.Anchor);
                }
                else
                {
                    (l.X2, l.Y2) = (cx, cy);
                }
            }
            NotifyDocumentChanged();
            return snap;
        }

        // ----- Undo / redo -----

        [RelayCommand]
        private void Undo()
        {
            if (_undoStack.Count == 0)
                return;
            _redoStack.Push(Document.ToJson());
            Restore(_undoStack.Pop());
        }

        [RelayCommand]
        private void Redo()
        {
            if (_redoStack.Count == 0)
                return;
            _undoStack.Push(Document.ToJson());
            Restore(_redoStack.Pop());
        }

        private void Restore(string json)
        {
            Document = DrawDocument.FromJson(json);
            _lastUndoTag = null;
            Selection.RemoveWhere(id => Document.Elements.All(e => e.Id != id));
            RebuildLayers();
            NotifySelectionChanged();
            NotifyDocumentChanged();
        }

        // ----- Element commands -----

        [RelayCommand]
        private void DeleteSelected()
        {
            if (Selection.Count == 0)
                return;
            PushUndo();
            Document.Elements.RemoveAll(e => Selection.Contains(e.Id));
            foreach (var l in Document.Elements.OfType<LineElement>())
            {
                if (l.StartLink is Guid s && FindRect(s) == null) l.StartLink = null;
                if (l.EndLink is Guid t && FindRect(t) == null) l.EndLink = null;
            }
            RemoveEmptyGroups();
            Selection.Clear();
            NotifySelectionChanged();
            NotifyStructureChanged();
        }

        [RelayCommand]
        private void GroupSelected()
        {
            // The units being grouped: whole root groups (which become children of
            // the new group, preserving their subtree) and loose elements. A
            // partially selected group is pulled in completely.
            var rootGroups = new HashSet<Guid>();
            var loose = new List<DrawElement>();
            foreach (var el in SelectedElements)
            {
                if (RootGroupOf(el) is Guid root)
                    rootGroups.Add(root);
                else
                    loose.Add(el);
            }
            if (rootGroups.Count + loose.Count < 2)
                return;
            PushUndo();
            var group = new GroupInfo { Name = $"Group {Document.Groups.Count + 1}" };
            Document.Groups.Add(group);
            foreach (var rootId in rootGroups)
                FindGroup(rootId)!.ParentId = group.Id;
            foreach (var el in loose)
                el.GroupId = group.Id;
            // Keep the new group's elements contiguous in z-order, anchored at the
            // topmost member; relative order (and nested blocks) are preserved.
            var members = Document.Elements.Where(e => IsInSubtree(e.GroupId, group.Id)).ToList();
            int topIndex = Document.Elements.FindLastIndex(e => members.Contains(e));
            Document.Elements.RemoveAll(e => members.Contains(e));
            int insertAt = Math.Min(topIndex - (members.Count - 1), Document.Elements.Count);
            Document.Elements.InsertRange(Math.Max(0, insertAt), members);
            RemoveEmptyGroups();
            SetSelection(members.Select(m => m.Id));
            NotifyStructureChanged();
        }

        [RelayCommand]
        private void UngroupSelected()
        {
            // Dissolves the root group(s) covering the selection by one level:
            // their child groups and direct elements become top-level.
            var roots = new HashSet<Guid>();
            foreach (var el in SelectedElements)
                if (RootGroupOf(el) is Guid root)
                    roots.Add(root);
            if (roots.Count == 0)
                return;
            PushUndo();
            foreach (var rootId in roots)
            {
                foreach (var child in Document.Groups.Where(g => g.ParentId == rootId))
                    child.ParentId = null;
                foreach (var el in Document.Elements.Where(e => e.GroupId == rootId))
                    el.GroupId = null;
                Document.Groups.RemoveAll(g => g.Id == rootId);
            }
            RemoveEmptyGroups();
            NotifyStructureChanged();
        }

        private void RemoveEmptyGroups()
        {
            bool removed;
            do
            {
                removed = false;
                foreach (var g in Document.Groups.ToList())
                {
                    if (Document.Elements.Any(e => e.GroupId == g.Id) ||
                        Document.Groups.Any(c => c.ParentId == g.Id))
                        continue;
                    Document.Groups.Remove(g);
                    removed = true;
                }
            } while (removed);
        }

        /// <summary>Moves a dragged layer row (element or whole group subtree) to the
        /// gap above layer row <paramref name="gapRow"/> (Layers.Count = below the
        /// last row). The dropped unit joins the deepest group that encloses both of
        /// its new neighbors (null at top level), so dragging into a group's block
        /// nests, and dragging out un-nests.</summary>
        public void ReorderLayers(LayerItem dragged, int gapRow)
        {
            // Elements in displayed order (topmost first), with the visual position of
            // every row gap. Group header rows contribute no element of their own.
            var visual = new List<DrawElement>();
            var gapToVisual = new int[Layers.Count + 1];
            for (int i = 0; i < Layers.Count; i++)
            {
                gapToVisual[i] = visual.Count;
                if (!Layers[i].IsGroup)
                {
                    var el = Document.Elements.FirstOrDefault(e => e.Id == Layers[i].Id);
                    if (el != null)
                        visual.Add(el);
                }
            }
            gapToVisual[Layers.Count] = visual.Count;
            int insertAt = gapToVisual[Math.Clamp(gapRow, 0, Layers.Count)];

            var block = dragged.IsGroup
                ? visual.Where(e => IsInSubtree(e.GroupId, dragged.Id)).ToList()
                : visual.Where(e => e.Id == dragged.Id).ToList();
            if (block.Count == 0)
                return;

            int removedBefore = block.Count(b => visual.IndexOf(b) < insertAt);
            var rest = visual.Where(e => !block.Contains(e)).ToList();
            int pos = Math.Clamp(insertAt - removedBefore, 0, rest.Count);

            // The deepest group containing both neighbors of the insertion point.
            var above = pos > 0 ? rest[pos - 1] : null;
            var below = pos < rest.Count ? rest[pos] : null;
            Guid? targetGroup = null;
            if (above != null && below != null)
            {
                var belowChain = AncestorGroups(below.GroupId).ToHashSet();
                targetGroup = AncestorGroups(above.GroupId)
                    .Where(belowChain.Contains)
                    .Cast<Guid?>()
                    .FirstOrDefault();
            }

            Guid? oldParent = dragged.IsGroup ? FindGroup(dragged.Id)?.ParentId : block[0].GroupId;
            bool parentChanges = oldParent != targetGroup;

            var newVisual = new List<DrawElement>(rest);
            newVisual.InsertRange(pos, block);
            if (!parentChanges && newVisual.SequenceEqual(visual))
                return;

            PushUndo();
            if (dragged.IsGroup)
            {
                var g = FindGroup(dragged.Id);
                if (g != null)
                    g.ParentId = targetGroup;
            }
            else
            {
                block[0].GroupId = targetGroup;
            }
            Document.Elements.Clear();
            for (int i = newVisual.Count - 1; i >= 0; i--)
                Document.Elements.Add(newVisual[i]);
            RemoveEmptyGroups();
            NotifyStructureChanged();
        }

        // ----- Layers panel -----

        public void RebuildLayers()
        {
            Layers.Clear();
            EmitLayerScope(null, 0);
            LayersRebuilt?.Invoke();
        }

        /// <summary>Emits the rows of one tree level: the child groups and direct
        /// elements of <paramref name="parentId"/>, topmost first (a group sorts by
        /// its topmost subtree element), recursing into each group.</summary>
        private void EmitLayerScope(Guid? parentId, int depth)
        {
            var units = new List<(int TopIndex, GroupInfo? Group, DrawElement? Element)>();
            foreach (var g in Document.Groups.Where(g => g.ParentId == parentId))
            {
                int top = -1;
                for (int i = Document.Elements.Count - 1; i >= 0; i--)
                {
                    if (IsInSubtree(Document.Elements[i].GroupId, g.Id))
                    {
                        top = i;
                        break;
                    }
                }
                if (top >= 0)
                    units.Add((top, g, null));
            }
            for (int i = 0; i < Document.Elements.Count; i++)
            {
                if (Document.Elements[i].GroupId == parentId)
                    units.Add((i, null, Document.Elements[i]));
            }

            foreach (var unit in units.OrderByDescending(u => u.TopIndex))
            {
                if (unit.Group != null)
                {
                    Layers.Add(new LayerItem
                    {
                        Id = unit.Group.Id,
                        IsGroup = true,
                        Icon = "▣",
                        Name = unit.Group.Name,
                        Margin = new Thickness(depth * 18, 0, 0, 0),
                    });
                    EmitLayerScope(unit.Group.Id, depth + 1);
                }
                else
                {
                    Layers.Add(MakeLayerItem(unit.Element!, depth));
                }
            }
        }

        private static LayerItem MakeLayerItem(DrawElement el, int depth) => new()
        {
            Id = el.Id,
            IsGroup = false,
            Icon = el switch
            {
                RectElement { IsTextBox: true } => "T",
                RectElement => "▭",
                _ => "╱",
            },
            Name = el.Name,
            Margin = new Thickness(depth * 18, 0, 0, 0),
        };

        // ----- Properties panel proxies -----

        public string[] BorderStyles { get; } = { "Normal", "Bold", "Double", "None" };
        public string[] LineStyles { get; } = { "Normal", "Bold", "Double" };
        public string[] FillStyles { get; } = { "Transparent", "Solid" };
        public string[] ArrowStyles { get; } = { "None", "Triangle" };
        public string[] VAligns { get; } = { "Top", "Center", "Bottom" };
        public string[] HAligns { get; } = { "Left", "Center", "Right" };

        public string? SelGroupName
        {
            get => SelectedGroup?.Name;
            set
            {
                var g = SelectedGroup;
                if (g == null || value == null || g.Name == value)
                    return;
                PushUndo("gname:" + g.Id);
                g.Name = value;
                var li = Layers.FirstOrDefault(l => l.IsGroup && l.Id == g.Id);
                if (li != null)
                    li.Name = value;
                NotifyDocumentChanged();
            }
        }

        public string? SelName
        {
            get => SingleSelected?.Name;
            set
            {
                var el = SingleSelected;
                if (el == null || value == null || el.Name == value)
                    return;
                PushUndo("name:" + el.Id);
                el.Name = value;
                var li = Layers.FirstOrDefault(l => l.Id == el.Id);
                if (li != null)
                    li.Name = value;
                NotifyDocumentChanged();
            }
        }

        public string? SelBorderStyle
        {
            get => SingleRect?.LineStyle.ToString();
            set
            {
                var r = SingleRect;
                if (r == null || value == null || r.LineStyle.ToString() == value)
                    return;
                PushUndo();
                r.LineStyle = Enum.Parse<LineStyle>(value);
                NotifyDocumentChanged();
            }
        }

        public string? SelFill
        {
            get => SingleRect?.FillStyle.ToString();
            set
            {
                var r = SingleRect;
                if (r == null || value == null || r.FillStyle.ToString() == value)
                    return;
                PushUndo();
                r.FillStyle = Enum.Parse<FillStyle>(value);
                NotifyDocumentChanged();
            }
        }

        public string? SelVAlign
        {
            get => SingleRect?.VerticalAlign.ToString();
            set
            {
                var r = SingleRect;
                if (r == null || value == null || r.VerticalAlign.ToString() == value)
                    return;
                PushUndo();
                r.VerticalAlign = Enum.Parse<VAlign>(value);
                NotifyDocumentChanged();
            }
        }

        public string? SelHAlign
        {
            get => SingleRect?.HorizontalAlign.ToString();
            set
            {
                var r = SingleRect;
                if (r == null || value == null || r.HorizontalAlign.ToString() == value)
                    return;
                PushUndo();
                r.HorizontalAlign = Enum.Parse<HAlign>(value);
                NotifyDocumentChanged();
            }
        }

        public string? SelText
        {
            get => SingleRect?.Text;
            set
            {
                var r = SingleRect;
                if (r == null || value == null || r.Text == value)
                    return;
                PushUndo("text:" + r.Id);
                r.Text = value;
                NotifyDocumentChanged();
            }
        }

        public string? SelLineStyle
        {
            get => SingleLine?.LineStyle.ToString();
            set
            {
                var l = SingleLine;
                if (l == null || value == null || l.LineStyle.ToString() == value)
                    return;
                PushUndo();
                l.LineStyle = Enum.Parse<LineStyle>(value);
                NotifyDocumentChanged();
            }
        }

        public string? SelStartArrow
        {
            get => SingleLine?.StartArrow.ToString();
            set
            {
                var l = SingleLine;
                if (l == null || value == null || l.StartArrow.ToString() == value)
                    return;
                PushUndo();
                l.StartArrow = Enum.Parse<ArrowStyle>(value);
                NotifyDocumentChanged();
            }
        }

        public string? SelEndArrow
        {
            get => SingleLine?.EndArrow.ToString();
            set
            {
                var l = SingleLine;
                if (l == null || value == null || l.EndArrow.ToString() == value)
                    return;
                PushUndo();
                l.EndArrow = Enum.Parse<ArrowStyle>(value);
                NotifyDocumentChanged();
            }
        }

        // ----- File commands -----

        private static IStorageProvider? Storage =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                .MainWindow?.StorageProvider;

        private static readonly FilePickerFileType AsciiDrawFileType =
            new("AsciiDraw drawing") { Patterns = new[] { "*.asciidraw" } };

        private static Avalonia.Controls.Window? MainWindow =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        private string CurrentFileName =>
            _filePath != null ? Path.GetFileName(_filePath) : "unnamed.asciidraw";

        /// <summary>When the document has unsaved changes, asks the user to save them.
        /// Returns true when it is safe to discard the current document.</summary>
        public async Task<bool> ConfirmLoseChangesAsync()
        {
            if (!IsDirty)
                return true;
            var owner = MainWindow;
            if (owner == null)
                return true;
            var choice = await Views.ConfirmSaveDialog.ShowAsync(owner, CurrentFileName);
            return choice switch
            {
                Views.SaveChoice.Save => await SaveCoreAsync(),
                Views.SaveChoice.DontSave => true,
                _ => false,
            };
        }

        [RelayCommand]
        private async Task NewFile()
        {
            if (!await ConfirmLoseChangesAsync())
                return;
            Document = new DrawDocument();
            _undoStack.Clear();
            _redoStack.Clear();
            _lastUndoTag = null;
            _filePath = null;
            Selection.Clear();
            NotifySelectionChanged();
            NotifyStructureChanged();
            IsDirty = false;
            UpdateTitle();
        }

        [RelayCommand]
        private async Task Open()
        {
            if (!await ConfirmLoseChangesAsync())
                return;
            var storage = Storage;
            if (storage == null)
                return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Drawing",
                FileTypeFilter = new[] { AsciiDrawFileType },
            });
            if (files.Count == 0)
                return;
            try
            {
                await using var stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                Document = DrawDocument.FromJson(await reader.ReadToEndAsync());
                _filePath = files[0].TryGetLocalPath();
                _undoStack.Clear();
                _redoStack.Clear();
                _lastUndoTag = null;
                Selection.Clear();
                NotifySelectionChanged();
                NotifyStructureChanged();
                IsDirty = false;
                UpdateTitle();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                SelectionStatus = "Failed to open file: " + ex.Message;
            }
        }

        [RelayCommand]
        private async Task Save()
        {
            await SaveCoreAsync();
        }

        /// <summary>Saves the document, prompting for a path when there is none.
        /// Returns false when the user cancelled the file picker.</summary>
        private async Task<bool> SaveCoreAsync()
        {
            if (_filePath == null)
                return await SaveAsCoreAsync();
            await File.WriteAllTextAsync(_filePath, Document.ToJson());
            IsDirty = false;
            SelectionStatus = $"Saved {Path.GetFileName(_filePath)}";
            return true;
        }

        private async Task<bool> SaveAsCoreAsync()
        {
            var storage = Storage;
            if (storage == null)
                return false;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Drawing",
                SuggestedFileName = "unnamed.asciidraw",
                DefaultExtension = "asciidraw",
                FileTypeChoices = new[] { AsciiDrawFileType },
            });
            if (file == null)
                return false;
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(Document.ToJson());
            }
            _filePath = file.TryGetLocalPath();
            IsDirty = false;
            UpdateTitle();
            SelectionStatus = $"Saved {file.Name}";
            return true;
        }

        private static Avalonia.Input.Platform.IClipboard? Clipboard =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                .MainWindow?.Clipboard;

        private static async Task SetClipboardTextAsync(string text)
        {
            var clipboard = Clipboard;
            if (clipboard == null)
                return;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(DataFormat.Text, text));
            await clipboard.SetDataAsync(transfer);
        }

        [RelayCommand]
        private async Task CopyText()
        {
            await SetClipboardTextAsync(Exporter.ToText(Document));
            SelectionStatus = "Copied to clipboard";
        }

        // ----- Copy / paste elements -----

        [RelayCommand]
        private async Task CopySelection()
        {
            if (Selection.Count == 0)
                return;
            // Clipboard payload is a DrawDocument holding the selected elements plus
            // the fully selected groups (the JSON round-trip also deep-clones, so the
            // sanitizing below never touches live objects).
            var includedGroups = Document.Groups
                .Where(g => IsGroupFullySelected(g.Id))
                .Select(g => g.Id)
                .ToHashSet();
            var payload = DrawDocument.FromJson(new DrawDocument
            {
                Elements = SelectedElements.ToList(),
                Groups = Document.Groups.Where(g => includedGroups.Contains(g.Id)).ToList(),
            }.ToJson());
            foreach (var el in payload.Elements)
                if (el.GroupId is Guid g && !includedGroups.Contains(g))
                    el.GroupId = null;
            foreach (var g in payload.Groups)
                if (g.ParentId is Guid pid && !includedGroups.Contains(pid))
                    g.ParentId = null;
            await SetClipboardTextAsync(payload.ToJson());
            SelectionStatus = $"Copied {payload.Elements.Count} elements";
        }

        [RelayCommand]
        private async Task Paste()
        {
            var clipboard = Clipboard;
            if (clipboard == null)
                return;
            string? text = null;
            using (var data = await clipboard.TryGetDataAsync())
            {
                if (data != null)
                    text = await data.TryGetTextAsync();
            }
            if (string.IsNullOrWhiteSpace(text))
                return;
            DrawDocument payload;
            try
            {
                payload = DrawDocument.FromJson(text);
            }
            catch (JsonException)
            {
                SelectionStatus = "Clipboard has no AsciiDraw content";
                return;
            }
            if (payload.Elements.Count == 0)
                return;

            PushUndo();
            var groupMap = payload.Groups.ToDictionary(g => g.Id, _ => Guid.NewGuid());
            var elementMap = payload.Elements.ToDictionary(e => e.Id, _ => Guid.NewGuid());
            var usedElementNames = Document.Elements.Select(e => e.Name).ToHashSet();
            var usedGroupNames = Document.Groups.Select(g => g.Name).ToHashSet();

            foreach (var g in payload.Groups)
            {
                g.Id = groupMap[g.Id];
                g.ParentId = g.ParentId is Guid pid && groupMap.TryGetValue(pid, out var np)
                    ? np
                    : null;
                g.Name = UniqueName(g.Name, usedGroupNames);
            }
            foreach (var el in payload.Elements)
            {
                el.Id = elementMap[el.Id];
                el.GroupId = el.GroupId is Guid gid && groupMap.TryGetValue(gid, out var ng)
                    ? ng
                    : null;
                el.Name = UniqueName(el.Name, usedElementNames);
                el.Translate(2, 2);
                if (el is LineElement l)
                {
                    l.StartLink = l.StartLink is Guid s && elementMap.TryGetValue(s, out var ns)
                        ? ns
                        : null;
                    l.EndLink = l.EndLink is Guid t && elementMap.TryGetValue(t, out var nt)
                        ? nt
                        : null;
                }
            }

            Document.Groups.AddRange(payload.Groups);
            Document.Elements.AddRange(payload.Elements);
            SyncLinkedLines();
            SetSelection(payload.Elements.Select(e => e.Id));
            NotifyStructureChanged();
            SelectionStatus = $"Pasted {payload.Elements.Count} elements";
        }

        /// <summary>"xyz" stays "xyz" when free; otherwise becomes "xyz (2)", "xyz (3)", …
        /// (an existing " (n)" suffix counts as part of the base "xyz").</summary>
        private static string UniqueName(string name, HashSet<string> used)
        {
            if (used.Add(name))
                return name;
            var baseName = System.Text.RegularExpressions.Regex.Replace(name, @" \(\d+\)$", "");
            for (int n = 2; ; n++)
            {
                var candidate = $"{baseName} ({n})";
                if (used.Add(candidate))
                    return candidate;
            }
        }

        [RelayCommand]
        private async Task ExportTxt()
        {
            await ExportString("txt", "Plain text", Exporter.ToText(Document));
        }

        [RelayCommand]
        private async Task ExportSvg()
        {
            await ExportString("svg", "SVG image", Exporter.ToSvg(Document));
        }

        private async Task ExportString(string ext, string label, string content)
        {
            var file = await PickExportFile(ext, label);
            if (file == null)
                return;
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);
            SelectionStatus = $"Exported {file.Name}";
        }

        [RelayCommand]
        private async Task ExportPng()
        {
            var file = await PickExportFile("png", "PNG image");
            if (file == null)
                return;
            await using var stream = await file.OpenWriteAsync();
            Exporter.ToPng(Document, stream);
            SelectionStatus = $"Exported {file.Name}";
        }

        private static async Task<IStorageFile?> PickExportFile(string ext, string label)
        {
            var storage = Storage;
            if (storage == null)
                return null;
            return await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Export as {ext.ToUpperInvariant()}",
                SuggestedFileName = "drawing." + ext,
                DefaultExtension = ext,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(label) { Patterns = new[] { "*." + ext } },
                },
            });
        }

        private void UpdateTitle()
        {
            WindowTitle = $"{CurrentFileName}{(IsDirty ? "*" : "")} - AsciiDraw";
        }
    }
}
