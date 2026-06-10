using System;
using System.Linq;
using AsciiDraw.ViewModels;
using Avalonia.Controls;

namespace AsciiDraw.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _vm;
        private bool _syncingLayers;

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
