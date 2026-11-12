using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ImageMark.Models;
using ImageMark.Services;

namespace ImageMark;

// Logó: presetek és az előnézeten húzható/méretezhető logóréteg.
public partial class MainWindow
{
    private const int LogoPreviewWidth = 1024;
    private const double LogoHandleSize = 11;
    private const double MinLogoSize = 16; // képernyőpontban, nagyítás nélkül

    private readonly ObservableCollection<LogoPreset> _logoPresets = new();
    private LogoPreset? _currentLogo;

    // A kiválasztott preset logója az előnézethez (a miniatűr is ezt mutatja).
    private string? _logoBitmapPath;
    private double _logoAspect = 1; // magasság / szélesség

    // Húzás az előnézeten; minden koordináta a scaleLayer (nagyítatlan kép) rendszerében.
    private enum LogoDrag { None, Move, Resize }
    private LogoDrag _logoDrag;
    private Rect _logoRect;
    private Point _logoGrabOffset;
    private Point _logoFixedCorner;
    private int _logoResizeDirX, _logoResizeDirY;

    private Rectangle[] LogoHandles => new[] { logoHandleTL, logoHandleTR, logoHandleBL, logoHandleBR };

    private bool LogoEnabled => tglLogo.IsChecked == true;

    private void InitializeLogo()
    {
        foreach (var p in LogoStore.Load(L.T("DefaultPresetName")))
            _logoPresets.Add(p);
        cmbLogoPreset.ItemsSource = _logoPresets;

        tglLogo.IsChecked = App.Settings.LogoEnabled;
        pnlLogo.IsEnabled = App.Settings.LogoEnabled;
        tglLogo.IsCheckedChanged += TglLogo_Changed;

        foreach (var slider in new[] { sldLogoSize, sldLogoOpacity, sldLogoMargin })
            slider.ValueChanged += LogoSetting_Changed;
        foreach (var radio in anchorGrid.Children.OfType<RadioButton>())
            radio.IsCheckedChanged += LogoSetting_Changed;

        imgLogo.Cursor = PanCursor;
        imgLogo.PointerPressed += Logo_PointerPressed;
        imgLogo.PointerMoved += Logo_PointerMoved;
        imgLogo.PointerReleased += Logo_PointerReleased;

        var cursors = new[]
        {
            StandardCursorType.TopLeftCorner, StandardCursorType.TopRightCorner,
            StandardCursorType.BottomLeftCorner, StandardCursorType.BottomRightCorner,
        };
        for (int i = 0; i < 4; i++)
        {
            var handle = LogoHandles[i];
            handle.Cursor = new Cursor(cursors[i]);
            handle.PointerPressed += Logo_PointerPressed;
            handle.PointerMoved += Logo_PointerMoved;
            handle.PointerReleased += Logo_PointerReleased;
        }

        // A keret és a fogantyúk csak akkor látszanak, amikor az egér az előnézet fölött van.
        previewHost.PointerEntered += (_, _) => UpdateLogoChrome();
        previewHost.PointerExited += (_, _) => UpdateLogoChrome();
        scaleLayer.SizeChanged += (_, _) => LayoutLogoOverlay();

        cmbLogoPreset.SelectedIndex = 0;
    }

    private void TglLogo_Changed(object? sender, RoutedEventArgs e)
    {
        pnlLogo.IsEnabled = LogoEnabled;
        App.Settings.LogoEnabled = LogoEnabled;
        App.Settings.Save();
        LayoutLogoOverlay();
    }

    // ─── Logó presetek ──────────────────────────────────────────

    private void CmbLogoPreset_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cmbLogoPreset.SelectedItem is LogoPreset preset)
        {
            if (preset != _currentLogo)
                expLogo.IsExpanded = false;
            _currentLogo = preset;
            LoadLogoPresetToUi(preset);
            RefreshLogoBitmap();
        }
    }

    private void LoadLogoPresetToUi(LogoPreset p)
    {
        _loadingUi = true;
        try
        {
            txtLogoPresetName.Text = p.Name;
            sldLogoSize.Value = p.SizePercent;
            sldLogoOpacity.Value = p.Opacity;
            sldLogoMargin.Value = p.MarginPercent;
            foreach (var radio in anchorGrid.Children.OfType<RadioButton>())
                radio.IsChecked = radio.Tag as string == p.Anchor.ToString();
        }
        finally
        {
            _loadingUi = false;
        }
    }

    private void ReadUiToLogoPreset(LogoPreset p)
    {
        p.SizePercent = sldLogoSize.Value;
        p.Opacity = sldLogoOpacity.Value;
        p.MarginPercent = sldLogoMargin.Value;
        var selected = anchorGrid.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (selected?.Tag is string tag)
            p.Anchor = int.Parse(tag);
    }

    private void LogoSetting_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loadingUi || _currentLogo == null)
            return;
        ReadUiToLogoPreset(_currentLogo);
        LayoutLogoOverlay();
    }

    private async void BtnChooseLogo_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentLogo == null)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("SelectLogoTitle"),
            FileTypeFilter = new[]
            {
                new FilePickerFileType(L.T("ImageFiles")) { Patterns = SupportedExtensions.Select(x => "*" + x).ToList() },
                new FilePickerFileType(L.T("AllFiles")) { Patterns = new[] { "*.*" } },
            },
        });
        if (LocalPaths(files).FirstOrDefault() is not { } path)
            return;

        try
        {
            using (WatermarkRenderer.LoadOriented(path, LogoPreviewWidth)) { } // olvasható kép-e
            _currentLogo.ImageFile = LogoStore.ImportImage(path);
            _currentLogo.ImageName = System.IO.Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, L.F("MsgLogoLoadFailed", ex.Message));
            return;
        }
        RefreshLogoBitmap();
    }

    private async void BtnSaveLogoPreset_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentLogo == null)
            return;
        var name = (txtLogoPresetName.Text ?? "").Trim();
        if (name.Length == 0)
        {
            await Dialogs.ShowMessageAsync(this, L.T("MsgNamePreset"));
            return;
        }
        _currentLogo.Name = name;
        ReadUiToLogoPreset(_currentLogo);
        LogoStore.Save(_logoPresets);

        // A lista újrakötése frissíti a megjelenített nevet.
        var current = _currentLogo;
        cmbLogoPreset.ItemsSource = null;
        cmbLogoPreset.ItemsSource = _logoPresets;
        cmbLogoPreset.SelectedItem = current;

        txtStatus.Text = L.F("StatusPresetSaved", name);
    }

    private void BtnNewLogoPreset_Click(object? sender, RoutedEventArgs e)
    {
        var clone = _currentLogo?.Clone() ?? new LogoPreset();
        var name = (txtLogoPresetName.Text ?? "").Trim();
        clone.Name = UniqueName(_logoPresets.Select(p => p.Name), name.Length > 0 ? name : L.T("NewPresetName"));
        _logoPresets.Add(clone);
        cmbLogoPreset.SelectedItem = clone;
        expLogo.IsExpanded = true; // új presethez rögtön logót kell választani
        LogoStore.Save(_logoPresets);
        txtStatus.Text = L.F("StatusPresetCreated", clone.Name);
    }

    private async void BtnDeleteLogoPreset_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentLogo == null)
            return;
        var target = _currentLogo;
        if (!await Dialogs.ConfirmAsync(this, L.F("MsgConfirmDelete", target.Name), L.T("DeletePreset")))
            return;

        _logoPresets.Remove(target);
        if (_logoPresets.Count == 0)
            _logoPresets.Add(new LogoPreset { Name = L.T("DefaultPresetName") });
        LogoStore.Save(_logoPresets);
        cmbLogoPreset.SelectedIndex = 0;
    }

    private void BtnResetLogo_Click(object? sender, RoutedEventArgs e)
    {
        if (_previewItem == null)
            return;
        _previewItem.LogoOverride = null;
        btnResetLogo.IsEnabled = false;
        LayoutLogoOverlay();
    }

    /// <summary>A kiválasztott preset logójának betöltése az előnézethez.</summary>
    private async void RefreshLogoBitmap()
    {
        txtLogoFile.Text = _currentLogo is { ImageName.Length: > 0 } preset ? preset.ImageName : L.T("NoLogo");

        string? path = _currentLogo == null ? null : LogoStore.ResolvePath(_currentLogo);
        if (path == _logoBitmapPath)
        {
            LayoutLogoOverlay();
            return;
        }
        _logoBitmapPath = path;

        Bitmap? bitmap = null;
        double aspect = 1;
        if (path != null)
        {
            try
            {
                (bitmap, aspect) = await Task.Run(() =>
                {
                    using var logo = WatermarkRenderer.LoadOriented(path, LogoPreviewWidth);
                    return (WatermarkRenderer.ToAvaloniaBitmap(logo), (double)logo.Height / logo.Width);
                });
            }
            catch (Exception ex)
            {
                txtStatus.Text = L.F("MsgLogoLoadFailed", ex.Message);
            }
        }

        if (path != _logoBitmapPath) // közben másik logó lett kiválasztva
        {
            bitmap?.Dispose();
            return;
        }

        var old = imgLogo.Source as IDisposable;
        imgLogo.Source = bitmap;
        imgLogoThumb.Source = bitmap;
        old?.Dispose();
        _logoAspect = aspect;
        LayoutLogoOverlay();
    }

    // ─── Logóréteg az előnézeten ────────────────────────────────

    /// <summary>A logó, a keret és a fogantyúk elhelyezése az éppen látszó képen.</summary>
    private void LayoutLogoOverlay()
    {
        var fit = scaleLayer.Bounds.Size;
        bool show = LogoEnabled && _currentLogo != null && _previewItem != null
            && imgLogo.Source != null && imgPreview.Source != null
            && fit.Width > 0 && fit.Height > 0;
        logoLayer.IsVisible = show;
        if (!show)
            return;

        var placement = _previewItem!.LogoOverride
            ?? LogoPlacement.FromPreset(_currentLogo!, fit.Width, fit.Height, _logoAspect);
        double width = placement.Width * fit.Width;
        _logoRect = new Rect(placement.X * fit.Width, placement.Y * fit.Height, width, width * _logoAspect);

        Place(imgLogo, _logoRect);
        imgLogo.Opacity = _currentLogo!.Opacity / 100;

        // A réteg a képpel együtt nagyítódik, a keret és a fogantyúk viszont maradjanak azonos méretűek.
        double handle = LogoHandleSize / _zoom;
        logoFrame.StrokeThickness = 1.5 / _zoom;
        Place(logoFrame, _logoRect);
        for (int i = 0; i < 4; i++)
        {
            double x = i % 2 == 0 ? _logoRect.Left : _logoRect.Right;
            double y = i < 2 ? _logoRect.Top : _logoRect.Bottom;
            LogoHandles[i].StrokeThickness = 1.5 / _zoom;
            Place(LogoHandles[i], new Rect(x - handle / 2, y - handle / 2, handle, handle));
        }
        UpdateLogoChrome();
    }

    private static void Place(Control control, Rect rect)
    {
        Canvas.SetLeft(control, rect.X);
        Canvas.SetTop(control, rect.Y);
        control.Width = rect.Width;
        control.Height = rect.Height;
    }

    private void UpdateLogoChrome()
    {
        bool visible = previewHost.IsPointerOver || _logoDrag != LogoDrag.None;
        logoFrame.IsVisible = visible;
        foreach (var handle in LogoHandles)
            handle.IsVisible = visible;
    }

    private void Logo_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(scaleLayer);
        if (control == imgLogo)
        {
            _logoDrag = LogoDrag.Move;
            _logoGrabOffset = new Point(position.X - _logoRect.X, position.Y - _logoRect.Y);
        }
        else
        {
            // A megfogott sarokkal átellenes sarok marad a helyén.
            int corner = int.Parse((string)control.Tag!);
            _logoDrag = LogoDrag.Resize;
            _logoResizeDirX = corner % 2 == 0 ? -1 : 1;
            _logoResizeDirY = corner < 2 ? -1 : 1;
            _logoFixedCorner = new Point(
                _logoResizeDirX > 0 ? _logoRect.Left : _logoRect.Right,
                _logoResizeDirY > 0 ? _logoRect.Top : _logoRect.Bottom);
        }
        e.Pointer.Capture(control);
        e.Handled = true; // különben az előnézet nagyítana/mozogna
    }

    private void Logo_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_logoDrag == LogoDrag.None || _previewItem == null)
            return;

        var fit = scaleLayer.Bounds.Size;
        var position = e.GetPosition(scaleLayer);
        Rect rect;
        if (_logoDrag == LogoDrag.Move)
        {
            // A logó a képen belül marad.
            double x = Math.Clamp(position.X - _logoGrabOffset.X, 0, Math.Max(0, fit.Width - _logoRect.Width));
            double y = Math.Clamp(position.Y - _logoGrabOffset.Y, 0, Math.Max(0, fit.Height - _logoRect.Height));
            rect = new Rect(x, y, _logoRect.Width, _logoRect.Height);
        }
        else
        {
            // Képarányt tartva méretez, a rögzített saroktól a kép széléig engedve.
            double maxWidth = Math.Min(
                _logoResizeDirX > 0 ? fit.Width - _logoFixedCorner.X : _logoFixedCorner.X,
                (_logoResizeDirY > 0 ? fit.Height - _logoFixedCorner.Y : _logoFixedCorner.Y) / _logoAspect);
            double width = Math.Max(
                (position.X - _logoFixedCorner.X) * _logoResizeDirX,
                (position.Y - _logoFixedCorner.Y) * _logoResizeDirY / _logoAspect);
            width = Math.Min(Math.Max(width, MinLogoSize), maxWidth);
            double height = width * _logoAspect;
            rect = new Rect(
                _logoResizeDirX > 0 ? _logoFixedCorner.X : _logoFixedCorner.X - width,
                _logoResizeDirY > 0 ? _logoFixedCorner.Y : _logoFixedCorner.Y - height,
                width, height);
        }

        _previewItem.LogoOverride = new LogoPlacement(rect.X / fit.Width, rect.Y / fit.Height, rect.Width / fit.Width);
        btnResetLogo.IsEnabled = true;
        LayoutLogoOverlay();
        e.Handled = true;
    }

    private void Logo_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_logoDrag == LogoDrag.None)
            return;
        _logoDrag = LogoDrag.None;
        e.Pointer.Capture(null);
        e.Handled = true;
        UpdateLogoChrome();
    }
}
