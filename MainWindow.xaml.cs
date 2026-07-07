using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMark.Models;
using ImageMark.Services;
using Microsoft.Win32;

namespace ImageMark;

public partial class MainWindow : Window
{
    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

    private readonly ObservableCollection<ImageItem> _images = new();
    private readonly ObservableCollection<WatermarkPreset> _presets = new();
    private readonly DispatcherTimer _previewTimer;

    private WatermarkPreset? _current;
    private bool _loadingUi;
    private string? _lastOutputDir;

    public MainWindow()
    {
        InitializeComponent();

        lstImages.ItemsSource = _images;
        cmbFont.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();

        foreach (var p in PresetStore.Load(L.T("DefaultPresetName")))
            _presets.Add(p);
        cmbPreset.ItemsSource = _presets;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RenderPreview(); };

        _loadingUi = true;
        foreach (ComboBoxItem item in cmbLanguage.Items)
        {
            if ((string)item.Tag == App.Settings.Language)
            {
                cmbLanguage.SelectedItem = item;
                break;
            }
        }
        if (cmbLanguage.SelectedItem == null)
            cmbLanguage.SelectedIndex = 0;
        _loadingUi = false;

        cmbPreset.SelectedIndex = 0;
    }

    // ─── Nyelv ──────────────────────────────────────────────────

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi)
            return;
        if (cmbLanguage.SelectedItem is ComboBoxItem { Tag: string code })
        {
            App.ApplyLanguage(code);
            App.Settings.Language = code;
            App.Settings.Save();
        }
    }

    // ─── Képlista ───────────────────────────────────────────────

    private void BtnAddImages_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = L.T("SelectImagesTitle"),
            Multiselect = true,
            Filter = L.T("ImagesFilter"),
        };
        if (dlg.ShowDialog() == true)
            AddImages(dlg.FileNames);
    }

    private void LstImages_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void LstImages_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            AddImages(files);
    }

    private void AddImages(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                continue;
            if (_images.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new ImageItem { Path = path };
            try
            {
                item.Thumbnail = WatermarkRenderer.LoadOriented(path, 96);
            }
            catch
            {
                continue; // nem olvasható kép, kihagyjuk
            }
            _images.Add(item);
            added++;
        }

        if (added > 0 && lstImages.SelectedItem == null)
            lstImages.SelectedIndex = 0;

        txtStatus.Text = added > 0
            ? L.F("StatusAdded", added, _images.Count)
            : L.T("StatusNoneAdded");
    }

    private void BtnRemoveImage_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in lstImages.SelectedItems.Cast<ImageItem>().ToList())
            _images.Remove(item);
        SchedulePreview();
    }

    private void BtnClearImages_Click(object sender, RoutedEventArgs e)
    {
        _images.Clear();
        SchedulePreview();
    }

    private void LstImages_SelectionChanged(object sender, SelectionChangedEventArgs e) => SchedulePreview();

    // ─── Presetek ───────────────────────────────────────────────

    private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbPreset.SelectedItem is WatermarkPreset preset)
        {
            _current = preset;
            LoadPresetToUi(preset);
            SchedulePreview();
        }
    }

    private void LoadPresetToUi(WatermarkPreset p)
    {
        _loadingUi = true;
        try
        {
            txtPresetName.Text = p.Name;
            txtText.Text = p.Text;
            cmbFont.SelectedItem = ((IEnumerable<FontFamily>)cmbFont.ItemsSource)
                .FirstOrDefault(f => f.Source == p.FontFamily);
            chkBold.IsChecked = p.Bold;
            chkItalic.IsChecked = p.Italic;
            sldSize.Value = p.FontSizePercent;
            sldOpacity.Value = p.Opacity;
            sldAngle.Value = p.Angle;
            txtColor.Text = p.ColorHex;
            sldSpacingX.Value = p.HorizontalSpacing;
            sldSpacingY.Value = p.VerticalSpacing;
            chkStagger.IsChecked = p.Stagger;
            UpdateColorPreview();
        }
        finally
        {
            _loadingUi = false;
        }
    }

    private void ReadUiToPreset(WatermarkPreset p)
    {
        p.Text = txtText.Text;
        if (cmbFont.SelectedItem is FontFamily ff)
            p.FontFamily = ff.Source;
        p.Bold = chkBold.IsChecked == true;
        p.Italic = chkItalic.IsChecked == true;
        p.FontSizePercent = sldSize.Value;
        p.Opacity = sldOpacity.Value;
        p.Angle = sldAngle.Value;
        if (IsValidColor(txtColor.Text))
            p.ColorHex = txtColor.Text.Trim();
        p.HorizontalSpacing = sldSpacingX.Value;
        p.VerticalSpacing = sldSpacingY.Value;
        p.Stagger = chkStagger.IsChecked == true;
    }

    private static bool IsValidColor(string text)
    {
        try
        {
            ColorConverter.ConvertFromString(text.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || _current == null)
            return;
        ReadUiToPreset(_current);
        UpdateColorPreview();
        SchedulePreview();
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
            txtColor.Text = hex;
    }

    private void UpdateColorPreview()
    {
        if (IsValidColor(txtColor.Text))
            rectColor.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(txtColor.Text.Trim()));
    }

    private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        var name = txtPresetName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, L.T("MsgNamePreset"), "ImageMark",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _current.Name = name;
        ReadUiToPreset(_current);
        PresetStore.Save(_presets);
        cmbPreset.Items.Refresh();
        txtStatus.Text = L.F("StatusPresetSaved", name);
    }

    private void BtnNewPreset_Click(object sender, RoutedEventArgs e)
    {
        var clone = _current?.Clone() ?? new WatermarkPreset();
        clone.Name = UniqueName(txtPresetName.Text.Trim().Length > 0 ? txtPresetName.Text.Trim() : L.T("NewPresetName"));
        _presets.Add(clone);
        cmbPreset.SelectedItem = clone;
        PresetStore.Save(_presets);
        txtStatus.Text = L.F("StatusPresetCreated", clone.Name);
    }

    private string UniqueName(string baseName)
    {
        if (_presets.All(p => p.Name != baseName))
            return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (_presets.All(p => p.Name != candidate))
                return candidate;
        }
    }

    private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        var result = MessageBox.Show(this, L.F("MsgConfirmDelete", _current.Name),
            "ImageMark", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _presets.Remove(_current);
        if (_presets.Count == 0)
            _presets.Add(new WatermarkPreset { Name = L.T("DefaultPresetName") });
        PresetStore.Save(_presets);
        cmbPreset.SelectedIndex = 0;
    }

    private void BtnExportPresets_Click(object sender, RoutedEventArgs e)
    {
        var select = new PresetSelectWindow(_presets) { Owner = this };
        if (select.ShowDialog() != true)
            return;

        var dlg = new SaveFileDialog
        {
            Title = L.T("ExportTitle"),
            Filter = L.T("PresetFileFilter"),
            FileName = "imagemark-presets.json",
            DefaultExt = ".json",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            PresetStore.ExportTo(dlg.FileName, select.Selected);
            txtStatus.Text = L.F("StatusExported", select.Selected.Count);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ImageMark", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnImportPresets_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = L.T("ImportTitle"),
            Filter = L.T("PresetFileFilter"),
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            var imported = PresetStore.ImportFrom(dlg.FileName);
            WatermarkPreset? first = null;
            foreach (var preset in imported)
            {
                preset.Name = UniqueName(string.IsNullOrWhiteSpace(preset.Name) ? L.T("NewPresetName") : preset.Name);
                _presets.Add(preset);
                first ??= preset;
            }
            PresetStore.Save(_presets);
            if (first != null)
                cmbPreset.SelectedItem = first;
            txtStatus.Text = L.F("StatusImported", imported.Count);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.F("MsgImportFailed", ex.Message), "ImageMark",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── Előnézet ───────────────────────────────────────────────

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RenderPreview()
    {
        var item = lstImages.SelectedItem as ImageItem ?? _images.FirstOrDefault();
        if (item == null || _current == null)
        {
            imgPreview.Source = null;
            txtPreviewHint.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var source = WatermarkRenderer.LoadOriented(item.Path, 1400);
            imgPreview.Source = WatermarkRenderer.Render(source, _current);
            txtPreviewHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            txtStatus.Text = L.F("StatusPreviewError", ex.Message);
        }
    }

    // ─── Kimenet / alkalmazás ───────────────────────────────────

    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = L.T("SelectOutputTitle") };
        if (dlg.ShowDialog() == true)
            txtOutputDir.Text = dlg.FolderName;
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOutputDir != null && Directory.Exists(_lastOutputDir))
            Process.Start("explorer.exe", _lastOutputDir);
    }

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        if (_images.Count == 0)
        {
            MessageBox.Show(this, L.T("MsgAddImagesFirst"), "ImageMark",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(txtText.Text))
        {
            MessageBox.Show(this, L.T("MsgEmptyText"), "ImageMark",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ReadUiToPreset(_current);
        var preset = _current.Clone();
        var files = _images.Select(i => i.Path).ToList();
        string outputDir = txtOutputDir.Text.Trim();
        string format = (cmbFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "orig";
        int quality = (int)sldQuality.Value;

        btnApply.IsEnabled = false;
        btnOpenFolder.Visibility = Visibility.Collapsed;
        progress.Visibility = Visibility.Visible;
        progress.Maximum = files.Count;
        progress.Value = 0;

        int done = 0;
        var errors = new List<string>();
        string? lastDir = null;

        try
        {
            await RunStaAsync(() =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        var source = WatermarkRenderer.LoadOriented(file);
                        var result = WatermarkRenderer.Render(source, preset);
                        string dir = outputDir.Length > 0 ? outputDir : Path.GetDirectoryName(file)!;
                        WatermarkRenderer.Save(result, file, dir, format, quality);
                        lastDir = dir;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }

                    int current = ++done;
                    Dispatcher.Invoke(() =>
                    {
                        progress.Value = current;
                        txtStatus.Text = L.F("StatusProcessing", current, files.Count);
                    });
                }
            });
        }
        finally
        {
            btnApply.IsEnabled = true;
            progress.Visibility = Visibility.Collapsed;
        }

        _lastOutputDir = lastDir;
        int success = files.Count - errors.Count;
        txtStatus.Text = errors.Count == 0
            ? L.F("StatusDone", success)
            : L.F("StatusDoneErrors", success, errors.Count);

        if (lastDir != null)
            btnOpenFolder.Visibility = Visibility.Visible;

        if (errors.Count > 0)
            MessageBox.Show(this, L.T("MsgErrorsHeader") + "\n\n" + string.Join("\n", errors),
                "ImageMark", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static Task RunStaAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

public class ImageItem
{
    public string Path { get; set; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public BitmapSource? Thumbnail { get; set; }
}
