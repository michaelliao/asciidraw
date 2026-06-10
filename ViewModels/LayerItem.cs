using System;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AsciiDraw.ViewModels
{
    /// <summary>One row in the layers panel: an element, or a group header.</summary>
    public partial class LayerItem : ObservableObject
    {
        public Guid Id { get; init; }
        public bool IsGroup { get; init; }
        public string Icon { get; init; } = "▭";
        public Thickness Margin { get; init; }

        [ObservableProperty]
        private string _name = "";
    }
}
