using System.Windows;
using ImageMark.Models;
using ImageMark.Services;

namespace ImageMark;

public partial class PresetSelectWindow : Window
{
    public List<WatermarkPreset> Selected { get; } = new();

    private readonly List<Item> _items;

    public PresetSelectWindow(IEnumerable<WatermarkPreset> presets)
    {
        InitializeComponent();
        _items = presets.Select(p => new Item(p)).ToList();
        lstPresets.ItemsSource = _items;
    }

    private void ChkSelectAll_Changed(object sender, RoutedEventArgs e)
    {
        if (_items == null)
            return;
        bool value = chkSelectAll.IsChecked == true;
        foreach (var item in _items)
            item.IsSelected = value;
        lstPresets.Items.Refresh();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsSelected).Select(i => i.Preset).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, L.T("MsgNoPresetSelected"), "ImageMark",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Selected.AddRange(selected);
        DialogResult = true;
    }

    private class Item
    {
        public Item(WatermarkPreset preset) => Preset = preset;
        public WatermarkPreset Preset { get; }
        public string Name => Preset.Name;
        public bool IsSelected { get; set; } = true;
    }
}
