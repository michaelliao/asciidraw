using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AsciiDraw.Views
{
    public enum SaveChoice
    {
        Save,
        DontSave,
        Cancel,
    }

    /// <summary>"Save changes?" prompt shown before discarding a dirty document.</summary>
    public static class ConfirmSaveDialog
    {
        public static async Task<SaveChoice> ShowAsync(Window owner, string fileName)
        {
            var choice = SaveChoice.Cancel;
            Window dialog = null!;

            Button Make(string text, SaveChoice c, bool isDefault = false, bool isCancel = false)
            {
                var b = new Button
                {
                    Content = text,
                    MinWidth = 90,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    IsDefault = isDefault,
                    IsCancel = isCancel,
                };
                b.Click += (_, _) =>
                {
                    choice = c;
                    dialog.Close();
                };
                return b;
            }

            dialog = new Window
            {
                Title = "AsciiDraw",
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Content = new StackPanel
                {
                    Margin = new Thickness(24, 20),
                    Spacing = 18,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Save changes to {fileName}?",
                            FontSize = 14,
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children =
                            {
                                Make("Save", SaveChoice.Save, isDefault: true),
                                Make("Don't Save", SaveChoice.DontSave),
                                Make("Cancel", SaveChoice.Cancel, isCancel: true),
                            },
                        },
                    },
                },
            };

            await dialog.ShowDialog(owner);
            return choice;
        }
    }
}
