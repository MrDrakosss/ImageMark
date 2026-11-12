using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using ImageMark.Models;
using ImageMark.Services;

namespace ImageMark;

/// <summary>Windows 11 stílusú, ablakon belüli párbeszédek.</summary>
internal static class Dialogs
{
    public static async Task ShowMessageAsync(Window owner, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = "ImageMark",
            Content = MessageContent(message),
            CloseButtonText = L.T("OK"),
            DefaultButton = FAContentDialogButton.Close,
        };
        await dialog.ShowAsync(owner);
    }

    public static async Task<bool> ConfirmAsync(Window owner, string message, string confirmText)
    {
        var dialog = new FAContentDialog
        {
            Title = "ImageMark",
            Content = MessageContent(message),
            PrimaryButtonText = confirmText,
            CloseButtonText = L.T("Cancel"),
            DefaultButton = FAContentDialogButton.Close,
        };
        return await dialog.ShowAsync(owner) == FAContentDialogResult.Primary;
    }

    /// <summary>Az exportálandó presetek kiválasztása; megszakításkor null.</summary>
    public static async Task<List<WatermarkPreset>?> SelectPresetsAsync(Window owner, IEnumerable<WatermarkPreset> presets)
    {
        var boxes = presets
            .Select(p => new CheckBox { Content = new TextBlock { Text = p.Name }, IsChecked = true, Tag = p })
            .ToList();
        var selectAll = new CheckBox { Content = L.T("SelectAll"), IsChecked = true };

        var list = new StackPanel();
        foreach (var box in boxes)
            list.Children.Add(box);

        var dialog = new FAContentDialog
        {
            Title = L.T("ExportTitle"),
            PrimaryButtonText = L.T("OK"),
            CloseButtonText = L.T("Cancel"),
            DefaultButton = FAContentDialogButton.Primary,
            Content = new StackPanel
            {
                MinWidth = 300,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = L.T("ExportSelectPrompt"), TextWrapping = TextWrapping.Wrap },
                    selectAll,
                    new ScrollViewer { MaxHeight = 280, Content = list },
                },
            },
        };

        selectAll.IsCheckedChanged += (_, _) =>
        {
            foreach (var box in boxes)
                box.IsChecked = selectAll.IsChecked == true;
        };
        foreach (var box in boxes)
            box.IsCheckedChanged += (_, _) => dialog.IsPrimaryButtonEnabled = boxes.Any(b => b.IsChecked == true);

        if (await dialog.ShowAsync(owner) != FAContentDialogResult.Primary)
            return null;
        return boxes.Where(b => b.IsChecked == true).Select(b => (WatermarkPreset)b.Tag!).ToList();
    }

    private static Control MessageContent(string message) => new ScrollViewer
    {
        MaxHeight = 360,
        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
    };
}
