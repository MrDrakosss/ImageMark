using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using ImageMark.Models;
using ImageMark.Services;
using SkiaSharp;

namespace ImageMark;

public partial class MainWindow : FAAppWindow
{
    private static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

    private const int PreviewWidth = 2048;
    private const int ThumbnailWidth = 112;

    private const double ClickZoom = 3;
    private const double MaxZoom = 8;
    private const double WheelZoomStep = 1.2;

    private static readonly Cursor ZoomCursor = new(StandardCursorType.Hand);
    private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);

    private readonly ObservableCollection<ImageItem> _images = new();
    private readonly ObservableCollection<WatermarkPreset> _presets = new();
    private readonly DispatcherTimer _previewTimer;

    private WatermarkPreset? _current;
    private bool _loadingUi;
    private string? _lastOutputDir;

    // Előnézet: a betöltött forráskép gyorsítótárazva, a renderelés háttérszálon fut.
    private SKBitmap? _previewSource;
    private string? _previewPath;
    private ImageItem? _previewItem; // az a kép, amelyik épp látszik az előnézetben
    private bool _previewBusy;
    private bool _previewPending;

    // Zoom: 1 = teljes kép látszik; az eltolás a previewHost koordinátáiban értendő.
    private readonly Transitions _panTransitions = CreateZoomTransitions();
    private readonly Transitions _scaleTransitions = CreateZoomTransitions();
    private double _zoom = 1;
    private double _panX, _panY;
    private Point? _pressPoint;
    private double _pressPanX, _pressPanY;
    private bool _dragging;

    public MainWindow()
    {
        InitializeComponent();
        UpdateTitle();

        TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.None };

        lstImages.ItemsSource = _images;
        cmbFont.ItemsSource = FontManager.Current.SystemFonts.OrderBy(f => f.Name).ToList();

        foreach (var p in PresetStore.Load(L.T("DefaultPresetName")))
            _presets.Add(p);
        cmbPreset.ItemsSource = _presets;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RenderPreview(); };

        txtText.TextChanging += Setting_Changed;
        txtColor.TextChanging += Setting_Changed;
        cmbFont.SelectionChanged += Setting_Changed;
        foreach (var check in new[] { chkBold, chkItalic, chkStagger })
            check.IsCheckedChanged += Setting_Changed;
        foreach (var slider in new[] { sldSize, sldOpacity, sldAngle, sldSpacingX, sldSpacingY })
            slider.ValueChanged += Setting_Changed;

        rootGrid.AddHandler(DragDrop.DragOverEvent, Root_DragOver);
        rootGrid.AddHandler(DragDrop.DropEvent, Root_Drop);

        previewHost.PointerPressed += Preview_PointerPressed;
        previewHost.PointerMoved += Preview_PointerMoved;
        previewHost.PointerReleased += Preview_PointerReleased;
        previewHost.PointerWheelChanged += Preview_PointerWheelChanged;
        previewHost.SizeChanged += (_, _) => ApplyZoom(animate: false);

        _loadingUi = true;
        SelectByTag(cmbLanguage, App.Settings.Language);
        SelectByTag(cmbTheme, App.Settings.Theme);
        tglWatermark.IsChecked = App.Settings.WatermarkEnabled;
        pnlWatermark.IsEnabled = App.Settings.WatermarkEnabled;
        _loadingUi = false;

        tglWatermark.IsCheckedChanged += TglWatermark_Changed;

        cmbPreset.SelectedIndex = 0;
        InitializeLogo();
    }

    private bool WatermarkEnabled => tglWatermark.IsChecked == true;

    private void TglWatermark_Changed(object? sender, RoutedEventArgs e)
    {
        pnlWatermark.IsEnabled = WatermarkEnabled;
        App.Settings.WatermarkEnabled = WatermarkEnabled;
        App.Settings.Save();
        SchedulePreview();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ActualTransparencyLevelProperty)
        {
            // Windows 11-en Mica háttér; máshol marad a téma tömör háttere.
            if (ActualTransparencyLevel == WindowTransparencyLevel.Mica)
            {
                TransparencyBackgroundFallback = Brushes.Transparent;
                Background = Brushes.Transparent;
            }
            else
            {
                ClearValue(BackgroundProperty);
            }
        }
    }

    /// <summary>A címsor a lokalizált név mellett a verziót is mutatja (forrása: ImageMark.csproj).</summary>
    private void UpdateTitle()
    {
        string? version = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Title = version == null ? L.T("AppTitle") : $"{L.T("AppTitle")} – v{version}";
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        var items = combo.Items.OfType<ComboBoxItem>().ToList();
        combo.SelectedItem = items.FirstOrDefault(i => i.Tag as string == tag) ?? items[0];
    }

    // ─── Nyelv és téma ──────────────────────────────────────────

    private void CmbLanguage_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi)
            return;
        if (cmbLanguage.SelectedItem is ComboBoxItem { Tag: string code })
        {
            App.ApplyLanguage(code);
            App.Settings.Language = code;
            App.Settings.Save();
            UpdateTitle();

            // A lenyílók kiválasztott eleme a régi nyelvű szöveget mutatná, újra kijelöljük.
            _loadingUi = true;
            foreach (var combo in new[] { cmbTheme, cmbFormat })
            {
                int index = combo.SelectedIndex;
                combo.SelectedIndex = -1;
                combo.SelectedIndex = index;
            }
            _loadingUi = false;
        }
    }

    private void CmbTheme_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi)
            return;
        if (cmbTheme.SelectedItem is ComboBoxItem { Tag: string code })
        {
            App.ApplyTheme(code);
            App.Settings.Theme = code;
            App.Settings.Save();
        }
    }

    // ─── Képlista ───────────────────────────────────────────────

    private async void BtnAddImages_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("SelectImagesTitle"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(L.T("ImageFiles")) { Patterns = SupportedExtensions.Select(x => "*" + x).ToList() },
                new FilePickerFileType(L.T("AllFiles")) { Patterns = new[] { "*.*" } },
            },
        });
        await AddImagesAsync(LocalPaths(files));
    }

    private void Root_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Root_Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is { } files)
            await AddImagesAsync(LocalPaths(files));
    }

    private static List<string> LocalPaths(IEnumerable<IStorageItem> items) =>
        items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();

    private async Task AddImagesAsync(List<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                continue;
            if (IsListed(path))
                continue;

            Bitmap thumbnail;
            try
            {
                thumbnail = await Task.Run(() =>
                {
                    using var bitmap = WatermarkRenderer.LoadOriented(path, ThumbnailWidth);
                    return WatermarkRenderer.ToAvaloniaBitmap(bitmap);
                });
            }
            catch
            {
                continue; // nem olvasható kép, kihagyjuk
            }

            if (IsListed(path)) // közben egy másik hozzáadás már felvette
                continue;
            _images.Add(new ImageItem { Path = path, Thumbnail = thumbnail });
            added++;

            if (lstImages.SelectedItem == null)
                lstImages.SelectedIndex = 0;
        }

        txtStatus.Text = added > 0
            ? L.F("StatusAdded", added, _images.Count)
            : L.T("StatusNoneAdded");
    }

    private bool IsListed(string path) =>
        _images.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

    private void BtnRemoveImage_Click(object? sender, RoutedEventArgs e)
    {
        if (lstImages.SelectedItems is { } selected)
        {
            foreach (var item in selected.Cast<ImageItem>().ToList())
                _images.Remove(item);
        }
        SchedulePreview();
    }

    private void BtnClearImages_Click(object? sender, RoutedEventArgs e)
    {
        _images.Clear();
        SchedulePreview();
    }

    private void LstImages_SelectionChanged(object? sender, SelectionChangedEventArgs e) => SchedulePreview();

    // ─── Presetek ───────────────────────────────────────────────

    private void CmbPreset_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cmbPreset.SelectedItem is WatermarkPreset preset)
        {
            // Kiválasztott presetnél a részletes beállítások összecsukva maradnak.
            if (preset != _current)
                expWatermark.IsExpanded = false;
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
            cmbFont.SelectedItem = ((IEnumerable<FontFamily>)cmbFont.ItemsSource!)
                .FirstOrDefault(f => f.Name == p.FontFamily);
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
        p.Text = txtText.Text ?? "";
        if (cmbFont.SelectedItem is FontFamily ff)
            p.FontFamily = ff.Name;
        p.Bold = chkBold.IsChecked == true;
        p.Italic = chkItalic.IsChecked == true;
        p.FontSizePercent = sldSize.Value;
        p.Opacity = sldOpacity.Value;
        p.Angle = sldAngle.Value;
        if (TryParseColor(txtColor.Text, out _))
            p.ColorHex = txtColor.Text!.Trim();
        p.HorizontalSpacing = sldSpacingX.Value;
        p.VerticalSpacing = sldSpacingY.Value;
        p.Stagger = chkStagger.IsChecked == true;
    }

    private static bool TryParseColor(string? text, out Color color) =>
        Color.TryParse((text ?? "").Trim(), out color);

    private void Setting_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loadingUi || _current == null)
            return;
        ReadUiToPreset(_current);
        UpdateColorPreview();
        SchedulePreview();
    }

    private void Swatch_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
            txtColor.Text = hex;
    }

    private void UpdateColorPreview()
    {
        if (TryParseColor(txtColor.Text, out var color))
            colorPreview.Background = new SolidColorBrush(color);
    }

    private async void BtnSavePreset_Click(object? sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        var name = (txtPresetName.Text ?? "").Trim();
        if (name.Length == 0)
        {
            await Dialogs.ShowMessageAsync(this, L.T("MsgNamePreset"));
            return;
        }
        _current.Name = name;
        ReadUiToPreset(_current);
        PresetStore.Save(_presets);

        // A lista újrakötése frissíti a megjelenített nevet.
        var current = _current;
        cmbPreset.ItemsSource = null;
        cmbPreset.ItemsSource = _presets;
        cmbPreset.SelectedItem = current;

        txtStatus.Text = L.F("StatusPresetSaved", name);
    }

    private void BtnNewPreset_Click(object? sender, RoutedEventArgs e)
    {
        var clone = _current?.Clone() ?? new WatermarkPreset();
        var name = (txtPresetName.Text ?? "").Trim();
        clone.Name = UniqueName(_presets.Select(p => p.Name), name.Length > 0 ? name : L.T("NewPresetName"));
        _presets.Add(clone);
        cmbPreset.SelectedItem = clone;
        expWatermark.IsExpanded = true; // új presetet rögtön szerkeszteni szokás
        PresetStore.Save(_presets);
        txtStatus.Text = L.F("StatusPresetCreated", clone.Name);
    }

    private static string UniqueName(IEnumerable<string> existing, string baseName)
    {
        var names = existing.ToHashSet();
        if (!names.Contains(baseName))
            return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private async void BtnDeletePreset_Click(object? sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        var target = _current;
        if (!await Dialogs.ConfirmAsync(this, L.F("MsgConfirmDelete", target.Name), L.T("DeletePreset")))
            return;

        _presets.Remove(target);
        if (_presets.Count == 0)
            _presets.Add(new WatermarkPreset { Name = L.T("DefaultPresetName") });
        PresetStore.Save(_presets);
        cmbPreset.SelectedIndex = 0;
    }

    private FilePickerFileType[] PresetFileTypes() => new[]
    {
        new FilePickerFileType(L.T("PresetFiles")) { Patterns = new[] { "*.json" } },
        new FilePickerFileType(L.T("AllFiles")) { Patterns = new[] { "*.*" } },
    };

    private async void BtnExportPresets_Click(object? sender, RoutedEventArgs e)
    {
        var selected = await Dialogs.SelectPresetsAsync(this, _presets);
        if (selected == null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("ExportTitle"),
            SuggestedFileName = "imagemark-presets.json",
            DefaultExtension = "json",
            FileTypeChoices = PresetFileTypes(),
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;

        try
        {
            PresetStore.ExportTo(path, selected);
            txtStatus.Text = L.F("StatusExported", selected.Count);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, ex.Message);
        }
    }

    private async void BtnImportPresets_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("ImportTitle"),
            FileTypeFilter = PresetFileTypes(),
        });
        if (LocalPaths(files).FirstOrDefault() is not { } path)
            return;

        try
        {
            var imported = PresetStore.ImportFrom(path);
            WatermarkPreset? first = null;
            foreach (var preset in imported)
            {
                preset.Name = UniqueName(_presets.Select(p => p.Name),
                    string.IsNullOrWhiteSpace(preset.Name) ? L.T("NewPresetName") : preset.Name);
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
            await Dialogs.ShowMessageAsync(this, L.F("MsgImportFailed", ex.Message));
        }
    }

    // ─── Előnézet ───────────────────────────────────────────────

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async void RenderPreview()
    {
        if (_previewBusy)
        {
            _previewPending = true; // a futó renderelés után újra lefut a friss beállításokkal
            return;
        }

        var item = lstImages.SelectedItem as ImageItem ?? _images.FirstOrDefault();
        if (item == null || _current == null)
        {
            SetPreviewImage(null, null);
            _previewSource?.Dispose();
            _previewSource = null;
            _previewPath = null;
            return;
        }

        // A vízjel belesül az előnézeti képbe; a logó külön, mozgatható rétegként ül fölötte.
        _previewBusy = true;
        string path = item.Path;
        var preset = WatermarkEnabled ? _current.Clone() : null;
        var cached = path == _previewPath ? _previewSource : null;
        try
        {
            var (source, bitmap) = await Task.Run(() =>
            {
                var src = cached ?? WatermarkRenderer.LoadOriented(path, PreviewWidth);
                try
                {
                    using var rendered = WatermarkRenderer.Render(src, preset);
                    return (src, WatermarkRenderer.ToAvaloniaBitmap(rendered));
                }
                catch
                {
                    if (src != cached)
                        src.Dispose();
                    throw;
                }
            });

            if (source != _previewSource)
            {
                _previewSource?.Dispose();
                _previewSource = source;
                _previewPath = path;
                _zoom = 1; // másik kép: vissza a teljes nézetre
            }
            SetPreviewImage(bitmap, item);
        }
        catch (Exception ex)
        {
            txtStatus.Text = L.F("StatusPreviewError", ex.Message);
        }
        finally
        {
            _previewBusy = false;
            if (_previewPending)
            {
                _previewPending = false;
                RenderPreview();
            }
        }
    }

    private void SetPreviewImage(Bitmap? bitmap, ImageItem? item)
    {
        var old = imgPreview.Source as IDisposable;
        imgPreview.Source = bitmap;
        old?.Dispose();
        _previewItem = item;
        btnResetLogo.IsEnabled = item?.LogoOverride != null;

        txtPreviewHint.IsVisible = bitmap == null;
        if (bitmap == null)
            _zoom = 1;
        ApplyZoom(animate: false);
    }

    // ─── Előnézet nagyítása ─────────────────────────────────────

    private static Transitions CreateZoomTransitions() => new()
    {
        new TransformOperationsTransition
        {
            Property = RenderTransformProperty,
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new CubicEaseOut(),
        },
    };

    /// <summary>A teljes nézetben látszó kép helye a previewHost-on belül.</summary>
    private Rect GetFitRect()
    {
        var host = previewHost.Bounds.Size;
        if (imgPreview.Source is not { } source || source.Size.Width <= 0 || source.Size.Height <= 0)
            return new Rect(host);

        double scale = Math.Min(host.Width / source.Size.Width, host.Height / source.Size.Height);
        double width = source.Size.Width * scale;
        double height = source.Size.Height * scale;
        return new Rect((host.Width - width) / 2, (host.Height - height) / 2, width, height);
    }

    /// <summary>Nagyítás úgy, hogy az <paramref name="anchor"/> alatti képrészlet a helyén marad.</summary>
    private void SetZoom(double zoom, Point anchor, bool animate)
    {
        // A scaleLayer pontosan a képet fedi (GetFitRect), és a bal felső sarka körül skálázódik.
        var fit = GetFitRect();
        zoom = Math.Clamp(zoom, 1, MaxZoom);
        double localX = (anchor.X - fit.X - _panX) / _zoom;
        double localY = (anchor.Y - fit.Y - _panY) / _zoom;
        _zoom = zoom;
        _panX = anchor.X - fit.X - localX * zoom;
        _panY = anchor.Y - fit.Y - localY * zoom;
        ApplyZoom(animate);
    }

    private void ApplyZoom(bool animate)
    {
        bool hasImage = imgPreview.Source != null;
        if (!hasImage || _zoom <= 1.001)
        {
            _zoom = 1;
            _panX = _panY = 0;
        }
        else
        {
            var fit = GetFitRect();
            var host = previewHost.Bounds.Size;
            _panX = ClampPan(_panX, fit.X, fit.Width, host.Width);
            _panY = ClampPan(_panY, fit.Y, fit.Height, host.Height);
        }

        panLayer.Transitions = animate ? _panTransitions : null;
        scaleLayer.Transitions = animate ? _scaleTransitions : null;

        var translate = TransformOperations.CreateBuilder(1);
        translate.AppendTranslate(_panX, _panY);
        panLayer.RenderTransform = translate.Build();

        var scale = TransformOperations.CreateBuilder(1);
        scale.AppendScale(_zoom, _zoom);
        scaleLayer.RenderTransform = scale.Build();

        LayoutLogoOverlay(); // a fogantyúk mérete a nagyítástól függ

        zoomBadge.IsVisible = _zoom > 1;
        txtZoom.Text = $"{_zoom * 100:0}%";
        previewHost.Cursor = !hasImage ? Cursor.Default : _zoom > 1 ? PanCursor : ZoomCursor;
    }

    /// <summary>Az eltolás korlátozása: a kép széle nem húzható beljebb a nézet szélénél.</summary>
    private double ClampPan(double pan, double start, double length, double viewport)
    {
        double scaled = length * _zoom;
        if (scaled <= viewport)
            return (viewport - scaled) / 2 - start; // kisebb a nézetnél: középre
        return Math.Clamp(pan, viewport - scaled - start, -start);
    }

    private void Preview_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (imgPreview.Source == null || !e.GetCurrentPoint(previewHost).Properties.IsLeftButtonPressed)
            return;
        _pressPoint = e.GetPosition(previewHost);
        _pressPanX = _panX;
        _pressPanY = _panY;
        _dragging = false;
        e.Pointer.Capture(previewHost);
    }

    private void Preview_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is not { } start || _zoom <= 1)
            return;
        var position = e.GetPosition(previewHost);
        if (!_dragging && Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) < 4)
            return; // ennyi elmozdulás még kattintásnak számít

        _dragging = true;
        _panX = _pressPanX + position.X - start.X;
        _panY = _pressPanY + position.Y - start.Y;
        ApplyZoom(animate: false);
    }

    private void Preview_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressPoint is not { } start)
            return;
        _pressPoint = null;
        e.Pointer.Capture(null);
        if (_dragging)
            return;

        // Kattintás: belenagyít a kattintott pontra, a következő kattintás visszaáll.
        if (_zoom > 1)
            SetZoom(1, start, animate: true);
        else
            SetZoom(ClickZoom, start, animate: true);
    }

    private void Preview_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (imgPreview.Source == null)
            return;
        SetZoom(_zoom * Math.Pow(WheelZoomStep, e.Delta.Y), e.GetPosition(previewHost), animate: false);
        e.Handled = true;
    }

    // ─── Kimenet / alkalmazás ───────────────────────────────────

    private async void BtnBrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T("SelectOutputTitle"),
        });
        if (LocalPaths(folders).FirstOrDefault() is { } path)
            txtOutputDir.Text = path;
    }

    private void BtnOpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_lastOutputDir != null && Directory.Exists(_lastOutputDir))
            Process.Start("explorer.exe", _lastOutputDir);
    }

    private async void BtnApply_Click(object? sender, RoutedEventArgs e)
    {
        if (_current == null)
            return;
        if (_images.Count == 0)
        {
            await Dialogs.ShowMessageAsync(this, L.T("MsgAddImagesFirst"));
            return;
        }
        bool useWatermark = WatermarkEnabled;
        bool useLogo = LogoEnabled;
        if (!useWatermark && !useLogo)
        {
            await Dialogs.ShowMessageAsync(this, L.T("MsgNothingEnabled"));
            return;
        }
        if (useWatermark && string.IsNullOrWhiteSpace(txtText.Text))
        {
            await Dialogs.ShowMessageAsync(this, L.T("MsgEmptyText"));
            return;
        }
        var logoPreset = useLogo ? _currentLogo?.Clone() : null;
        string? logoPath = logoPreset == null ? null : LogoStore.ResolvePath(logoPreset);
        if (useLogo && logoPath == null)
        {
            await Dialogs.ShowMessageAsync(this, L.T("MsgNoLogoImage"));
            return;
        }

        ReadUiToPreset(_current);
        var preset = useWatermark ? _current.Clone() : null;
        var files = _images.Select(i => (i.Path, i.LogoOverride)).ToList();
        string outputDir = (txtOutputDir.Text ?? "").Trim();
        string format = (cmbFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "orig";
        int quality = (int)sldQuality.Value;

        btnApply.IsEnabled = false;
        btnOpenFolder.IsVisible = false;
        progress.IsVisible = true;
        progress.Maximum = files.Count;
        progress.Value = 0;

        int done = 0;
        var errors = new List<string>();
        string? lastDir = null;

        try
        {
            await Task.Run(() =>
            {
                using var logo = logoPath != null ? WatermarkRenderer.LoadOriented(logoPath) : null;

                foreach (var (file, logoOverride) in files)
                {
                    try
                    {
                        using var source = WatermarkRenderer.LoadOriented(file);
                        // Képenként áthelyezett logó, különben a preset szerinti alaphelyzet.
                        var placement = logo == null ? null : logoOverride ?? LogoPlacement.FromPreset(
                            logoPreset!, source.Width, source.Height, (double)logo.Height / logo.Width);
                        using var result = WatermarkRenderer.Render(source, preset, logo, placement,
                            logoPreset?.Opacity ?? 100);
                        string dir = outputDir.Length > 0 ? outputDir : Path.GetDirectoryName(file)!;
                        WatermarkRenderer.Save(result, file, dir, format, quality);
                        lastDir = dir;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }

                    int current = ++done;
                    Dispatcher.UIThread.Invoke(() =>
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
            progress.IsVisible = false;
        }

        _lastOutputDir = lastDir;
        int success = files.Count - errors.Count;
        txtStatus.Text = errors.Count == 0
            ? L.F("StatusDone", success)
            : L.F("StatusDoneErrors", success, errors.Count);

        if (lastDir != null)
            btnOpenFolder.IsVisible = true;

        if (errors.Count > 0)
            await Dialogs.ShowMessageAsync(this, L.T("MsgErrorsHeader") + "\n\n" + string.Join("\n", errors));
    }
}

public class ImageItem
{
    public string Path { get; set; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public Bitmap? Thumbnail { get; set; }

    /// <summary>A logó képenként áthelyezett/átméretezett helye; null = a preset szerinti.</summary>
    public LogoPlacement? LogoOverride { get; set; }
}
