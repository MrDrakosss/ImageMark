using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMark.Models;

namespace ImageMark.Services;

public static class WatermarkRenderer
{
    /// <summary>Kép betöltése az EXIF forgatás figyelembevételével.</summary>
    public static BitmapSource LoadOriented(string path, int? decodePixelWidth = null)
    {
        var rotation = GetExifRotation(path);

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.Rotation = rotation;
        if (decodePixelWidth.HasValue)
            bmp.DecodePixelWidth = decodePixelWidth.Value;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static Rotation GetExifRotation(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (frame.Metadata is BitmapMetadata md && md.ContainsQuery("System.Photo.Orientation"))
            {
                var value = md.GetQuery("System.Photo.Orientation");
                return Convert.ToInt32(value, CultureInfo.InvariantCulture) switch
                {
                    3 => Rotation.Rotate180,
                    6 => Rotation.Rotate90,
                    8 => Rotation.Rotate270,
                    _ => Rotation.Rotate0,
                };
            }
        }
        catch
        {
            // metaadat nélkül forgatás nélkül töltünk
        }
        return Rotation.Rotate0;
    }

    /// <summary>A kép és a rá csempézett vízjel összerenderelése.</summary>
    public static BitmapSource Render(BitmapSource source, WatermarkPreset preset)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));

            if (!string.IsNullOrWhiteSpace(preset.Text))
                DrawTiledText(dc, width, height, preset);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static void DrawTiledText(DrawingContext dc, double width, double height, WatermarkPreset preset)
    {
        double fontSize = Math.Max(6, width * preset.FontSizePercent / 100.0);

        var typeface = new Typeface(
            new FontFamily(preset.FontFamily),
            preset.Italic ? FontStyles.Italic : FontStyles.Normal,
            preset.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(preset.ColorHex);
        }
        catch
        {
            color = Colors.Gray;
        }

        byte alpha = (byte)Math.Clamp(preset.Opacity * 255 / 100.0, 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();

        var text = new FormattedText(
            preset.Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            1.0);

        double stepX = text.Width + fontSize * preset.HorizontalSpacing;
        double stepY = text.Height + fontSize * preset.VerticalSpacing;
        if (stepX <= 1 || stepY <= 1)
            return;

        double cx = width / 2;
        double cy = height / 2;
        double diagonal = Math.Sqrt(width * width + height * height);

        // Negatív szög = jobbra felfelé emelkedő szöveg (mint a mintaképen).
        dc.PushTransform(new RotateTransform(-preset.Angle, cx, cy));

        int row = 0;
        for (double y = cy - diagonal; y < cy + diagonal; y += stepY, row++)
        {
            double offset = preset.Stagger && row % 2 == 1 ? stepX / 2 : 0;
            for (double x = cx - diagonal - offset; x < cx + diagonal; x += stepX)
                dc.DrawText(text, new Point(x, y));
        }

        dc.Pop();
    }

    /// <summary>Mentés a kért formátumban ("jpg", "png" vagy "orig").</summary>
    public static string Save(BitmapSource bitmap, string sourcePath, string targetDir, string format, int jpegQuality)
    {
        string sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
        string targetExt = format switch
        {
            "jpg" => ".jpg",
            "png" => ".png",
            _ => sourceExt is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" ? sourceExt : ".png",
        };

        BitmapEncoder encoder = targetExt switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = jpegQuality },
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };

        string name = Path.GetFileNameWithoutExtension(sourcePath);
        string target = Path.Combine(targetDir, name + targetExt);

        // Ha a célfájl már létezik (vagy épp az eredeti lenne), "_watermark" utótagot kap.
        // Az eredeti fájlt soha nem írjuk felül.
        if (File.Exists(target) || IsSamePath(target, sourcePath))
        {
            target = Path.Combine(targetDir, name + "_watermark" + targetExt);
            if (IsSamePath(target, sourcePath))
                target = Path.Combine(targetDir, name + "_watermark2" + targetExt);
        }

        Directory.CreateDirectory(targetDir);
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(target);
        encoder.Save(fs);
        return target;
    }

    private static bool IsSamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
