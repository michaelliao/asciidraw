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
        public bool ShowPlaceholder => SingleSelected == null;

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

        /// <summary>Returns the ids that should be selected when this element is clicked
        /// (the whole group when the element belongs to one).</summary>
        public IReadOnlyList<Guid> ExpandToGroup(DrawElement el) =>
            el.GroupId is Guid g
                ? Document.Elements.Where(e => e.GroupId == g).Select(e => e.Id).ToList()
                : new List<Guid> { el.Id };

        public void SelectFromLayers(IEnumerable<LayerItem> items)
        {
            var ids = new HashSet<Guid>();
            foreach (var li in items)
            {
                if (li.IsGroup)
                    foreach (var el in Document.Elements.Where(e => e.GroupId == li.Id))
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
            OnPropertyChanged(nameof(ShowPlaceholder));
            OnPropertyChanged(nameof(SelectionHeader));
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

        public void NotifyDocumentChanged()
        {
            UpdateSelectionStatus();
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
            var sel = SelectedElements.ToList();
            if (sel.Count < 2)
                return;
            PushUndo();
            var group = new GroupInfo { Name = $"Group {Document.Groups.Count + 1}" };
            Document.Groups.Add(group);
            foreach (var el in sel)
                el.GroupId = group.Id;
            // Keep group members contiguous in z-order, anchored at the topmost member.
            int topIndex = Document.Elements.FindLastIndex(e => sel.Contains(e));
            Document.Elements.RemoveAll(e => sel.Contains(e));
            int insertAt = Math.Min(topIndex - (sel.Count - 1), Document.Elements.Count);
            Document.Elements.InsertRange(Math.Max(0, insertAt), sel);
            RemoveEmptyGroups();
            NotifyStructureChanged();
        }

        [RelayCommand]
        private void UngroupSelected()
        {
            var sel = SelectedElements.Where(e => e.GroupId != null).ToList();
            if (sel.Count == 0)
                return;
            PushUndo();
            foreach (var el in sel)
                el.GroupId = null;
            RemoveEmptyGroups();
            NotifyStructureChanged();
        }

        private void RemoveEmptyGroups()
        {
            Document.Groups.RemoveAll(g => Document.Elements.All(e => e.GroupId != g.Id));
        }

        /// <summary>Moves a dragged layer row (element or whole group) to the gap above
        /// layer row <paramref name="gapRow"/> (Layers.Count = below the last row).
        /// An element dropped strictly inside a group's block joins that group; one
        /// dragged out of its group leaves it. Groups never nest.</summary>
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
                ? visual.Where(e => e.GroupId == dragged.Id).ToList()
                : visual.Where(e => e.Id == dragged.Id).ToList();
            if (block.Count == 0)
                return;

            int removedBefore = block.Count(b => visual.IndexOf(b) < insertAt);
            var rest = visual.Where(e => !block.Contains(e)).ToList();
            int pos = Math.Clamp(insertAt - removedBefore, 0, rest.Count);

            var above = pos > 0 ? rest[pos - 1] : null;
            var below = pos < rest.Count ? rest[pos] : null;
            Guid? targetGroup = above?.GroupId != null && above.GroupId == below?.GroupId
                ? above.GroupId
                : null;

            if (dragged.IsGroup && targetGroup != null)
            {
                // Groups don't nest: drop below the surrounding block instead.
                while (pos < rest.Count && rest[pos].GroupId == targetGroup)
                    pos++;
                targetGroup = null;
            }

            Guid? newGroupId = dragged.IsGroup ? block[0].GroupId : targetGroup;
            bool groupChanges = !dragged.IsGroup && block[0].GroupId != newGroupId;

            var newVisual = new List<DrawElement>(rest);
            newVisual.InsertRange(pos, block);
            if (!groupChanges && newVisual.SequenceEqual(visual))
                return;

            PushUndo();
            if (!dragged.IsGroup)
                block[0].GroupId = newGroupId;
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
            var emitted = new HashSet<Guid>();
            for (int i = Document.Elements.Count - 1; i >= 0; i--)
            {
                var el = Document.Elements[i];
                if (emitted.Contains(el.Id))
                    continue;
                if (el.GroupId is Guid gid)
                {
                    var group = Document.Groups.FirstOrDefault(g => g.Id == gid);
                    Layers.Add(new LayerItem
                    {
                        Id = gid,
                        IsGroup = true,
                        Icon = "▣",
                        Name = group?.Name ?? "Group",
                        Margin = new Thickness(0),
                    });
                    for (int j = i; j >= 0; j--)
                    {
                        var member = Document.Elements[j];
                        if (member.GroupId == gid)
                        {
                            Layers.Add(MakeLayerItem(member, indented: true));
                            emitted.Add(member.Id);
                        }
                    }
                }
                else
                {
                    Layers.Add(MakeLayerItem(el, indented: false));
                    emitted.Add(el.Id);
                }
            }
            LayersRebuilt?.Invoke();
        }

        private static LayerItem MakeLayerItem(DrawElement el, bool indented) => new()
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
            Margin = new Thickness(indented ? 18 : 0, 0, 0, 0),
        };

        // ----- Properties panel proxies -----

        public string[] BorderStyles { get; } = { "Normal", "Bold", "Double", "None" };
        public string[] LineStyles { get; } = { "Normal", "Bold", "Double" };
        public string[] FillStyles { get; } = { "Transparent", "Solid" };
        public string[] ArrowStyles { get; } = { "None", "Triangle" };
        public string[] VAligns { get; } = { "Top", "Center", "Bottom" };
        public string[] HAligns { get; } = { "Left", "Center", "Right" };

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

        [RelayCommand]
        private void NewFile()
        {
            Document = new DrawDocument();
            _undoStack.Clear();
            _redoStack.Clear();
            _lastUndoTag = null;
            _filePath = null;
            Selection.Clear();
            UpdateTitle();
            NotifySelectionChanged();
            NotifyStructureChanged();
        }

        [RelayCommand]
        private async Task Open()
        {
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
                UpdateTitle();
                NotifySelectionChanged();
                NotifyStructureChanged();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                SelectionStatus = "Failed to open file: " + ex.Message;
            }
        }

        [RelayCommand]
        private async Task Save()
        {
            if (_filePath == null)
            {
                await SaveAs();
                return;
            }
            await File.WriteAllTextAsync(_filePath, Document.ToJson());
            SelectionStatus = $"Saved {Path.GetFileName(_filePath)}";
        }

        private async Task SaveAs()
        {
            var storage = Storage;
            if (storage == null)
                return;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Drawing",
                SuggestedFileName = "unnamed.asciidraw",
                DefaultExtension = "asciidraw",
                FileTypeChoices = new[] { AsciiDrawFileType },
            });
            if (file == null)
                return;
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(Document.ToJson());
            }
            _filePath = file.TryGetLocalPath();
            UpdateTitle();
            SelectionStatus = $"Saved {file.Name}";
        }

        [RelayCommand]
        private async Task CopyText()
        {
            var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                .MainWindow?.Clipboard;
            if (clipboard == null)
                return;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(DataFormat.Text, Exporter.ToText(Document)));
            await clipboard.SetDataAsync(transfer);
            SelectionStatus = "Copied to clipboard";
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
            var name = _filePath != null ? Path.GetFileName(_filePath) : "unnamed.asciidraw";
            WindowTitle = $"{name} - AsciiDraw";
        }
    }
}
