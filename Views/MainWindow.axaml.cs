using System;
using System.Linq;
using AsciiDraw.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AsciiDraw.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _vm;
        private bool _syncingLayers;
        private LayerItem? _dragLayerItem;
        private Point _layerPressPoint;
        private bool _layerDragActive;
        private int _layerDropGap = -1;

        public MainWindow()
        {
            InitializeComponent();

            TopRuler.Canvas = EditCanvas;
            LeftRuler.Canvas = EditCanvas;
            Scroller.ScrollChanged += (_, _) =>
            {
                TopRuler.Offset = Scroller.Offset.X;
                LeftRuler.Offset = Scroller.Offset.Y;
            };
            EditCanvas.MetricsChanged += () =>
            {
                TopRuler.InvalidateVisual();
                LeftRuler.InvalidateVisual();
            };

            LayerList.SelectionChanged += OnLayerSelectionChanged;
            LayerList.AddHandler(PointerPressedEvent, OnLayerPointerPressed, RoutingStrategies.Tunnel);
            LayerList.AddHandler(PointerMovedEvent, OnLayerPointerMoved, RoutingStrategies.Tunnel);
            LayerList.AddHandler(PointerReleasedEvent, OnLayerPointerReleased, RoutingStrategies.Tunnel);
            Opened += (_, _) => EditCanvas.Focus();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_vm != null)
            {
                _vm.SelectionChanged -= SyncLayerSelection;
                _vm.LayersRebuilt -= SyncLayerSelection;
            }
            _vm = DataContext as MainWindowViewModel;
            if (_vm != null)
            {
                _vm.SelectionChanged += SyncLayerSelection;
                _vm.LayersRebuilt += SyncLayerSelection;
                _vm.RebuildLayers();
            }
        }

        private void OnLayerSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_syncingLayers || _vm == null)
                return;
            _syncingLayers = true;
            try
            {
                _vm.SelectFromLayers(LayerList.SelectedItems!.Cast<LayerItem>().ToList());
            }
            finally
            {
                _syncingLayers = false;
            }
        }

        // ----- Layer drag-to-reorder -----

        private int LayerRowAt(Point p)
        {
            if (_vm == null)
                return -1;
            for (int i = 0; i < _vm.Layers.Count; i++)
            {
                if (LayerList.ContainerFromIndex(i) is { } c &&
                    c.TranslatePoint(default, LayerList) is { } top &&
                    p.Y >= top.Y && p.Y < top.Y + c.Bounds.Height)
                    return i;
            }
            return -1;
        }

        private int LayerGapAt(Point p)
        {
            if (_vm == null)
                return 0;
            for (int i = 0; i < _vm.Layers.Count; i++)
            {
                if (LayerList.ContainerFromIndex(i) is { } c &&
                    c.TranslatePoint(default, LayerList) is { } top &&
                    p.Y < top.Y + c.Bounds.Height / 2)
                    return i;
            }
            return _vm.Layers.Count;
        }

        private void OnLayerPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_vm == null || !e.GetCurrentPoint(LayerList).Properties.IsLeftButtonPressed)
                return;
            var p = e.GetPosition(LayerList);
            int row = LayerRowAt(p);
            _dragLayerItem = row >= 0 ? _vm.Layers[row] : null;
            _layerPressPoint = p;
            _layerDragActive = false;
            _layerDropGap = -1;
        }

        private void OnLayerPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_vm == null || _dragLayerItem == null)
                return;
            if (!e.GetCurrentPoint(LayerList).Properties.IsLeftButtonPressed)
                return;
            var p = e.GetPosition(LayerList);
            if (!_layerDragActive && Math.Abs(p.Y - _layerPressPoint.Y) < 5)
                return;
            _layerDragActive = true;
            _layerDropGap = LayerGapAt(p);
            UpdateLayerDropIndicator();
        }

        private void OnLayerPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            ClearLayerDropIndicator();
            if (_layerDragActive && _dragLayerItem != null && _vm != null && _layerDropGap >= 0)
                _vm.ReorderLayers(_dragLayerItem, _layerDropGap);
            _dragLayerItem = null;
            _layerDragActive = false;
            _layerDropGap = -1;
        }

        private void UpdateLayerDropIndicator()
        {
            if (_vm == null)
                return;
            ClearLayerDropIndicator();
            int n = _vm.Layers.Count;
            if (_layerDropGap < n)
                LayerList.ContainerFromIndex(_layerDropGap)?.Classes.Add("drop-above");
            else if (n > 0)
                LayerList.ContainerFromIndex(n - 1)?.Classes.Add("drop-below");
        }

        private void ClearLayerDropIndicator()
        {
            if (_vm == null)
                return;
            for (int i = 0; i < _vm.Layers.Count; i++)
            {
                if (LayerList.ContainerFromIndex(i) is { } c)
                {
                    c.Classes.Remove("drop-above");
                    c.Classes.Remove("drop-below");
                }
            }
        }

        private void SyncLayerSelection()
        {
            if (_syncingLayers || _vm == null)
                return;
            _syncingLayers = true;
            try
            {
                var items = LayerList.SelectedItems!;
                items.Clear();
                foreach (var li in _vm.Layers)
                {
                    bool selected = li.IsGroup
                        ? _vm.Document.Elements.Any(el => el.GroupId == li.Id) &&
                          _vm.Document.Elements.Where(el => el.GroupId == li.Id)
                              .All(el => _vm.Selection.Contains(el.Id))
                        : _vm.Selection.Contains(li.Id);
                    if (selected)
                        items.Add(li);
                }
            }
            finally
            {
                _syncingLayers = false;
            }
        }
    }
}
