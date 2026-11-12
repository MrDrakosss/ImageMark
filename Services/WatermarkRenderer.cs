using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMark.Models;
using SkiaSharp;
using Gdi = System.Drawing;

namespace ImageMark.Services;

public static class WatermarkRenderer
{
    /// <summary>Kép betöltése az EXIF forgatás figyelembevételével.</summary>
    public static SKBitmap LoadOriented(string path, int? maxWidth = null)
    {
        var (bitmap, orientation) = Decode(path, maxWidth);

        int degrees = orientation switch
        {
            3 => 180,
            6 => 90,
            8 => 270,
            _ => 0,
        };
        if (degrees != 0)
        {
            var rotated = Rotate(bitmap, degrees);
            bitmap.Dispose();
            bitmap = rotated;
        }

        if (maxWidth.HasValue && bitmap.Width > maxWidth.Value)
        {
            int height = Math.Max(1, (int)Math.Round(bitmap.Height * (double)maxWidth.Value / bitmap.Width));
            var resized = bitmap.Resize(
                new SKImageInfo(maxWidth.Value, height, SKColorType.Bgra8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (resized != null)
            {
                bitmap.Dispose();
                bitmap = resized;
            }
        }

        return bitmap;
    }

    private static (SKBitmap Bitmap, int Orientation) Decode(string path, int? maxWidth)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null)
            return DecodeWithGdi(path); // pl. TIFF, amit a Skia nem ismer

        int orientation = (int)codec.EncodedOrigin;
        var size = codec.Info.Size;
        if (maxWidth.HasValue)
        {
            // A JPEG dekóder tud kicsinyítve olvasni – nagy fotóknál sokkal gyorsabb.
            int orientedWidth = orientation is 6 or 8 ? size.Height : size.Width;
            float scale = maxWidth.Value / (float)orientedWidth;
            if (scale < 1)
                size = codec.GetScaledDimensions(Math.Min(1f, scale + 0.0625f));
        }

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new InvalidDataException($"{Path.GetFileName(path)}: {result}");
        }
        return (bitmap, orientation);
    }

    private static (SKBitmap Bitmap, int Orientation) DecodeWithGdi(string path)
    {
        const int ExifOrientationId = 0x0112;

        using var image = new Gdi.Bitmap(path);
        int orientation = 1;
        if (image.PropertyIdList.Contains(ExifOrientationId)
            && image.GetPropertyItem(ExifOrientationId)?.Value is { Length: >= 2 } value)
            orientation = BitConverter.ToUInt16(value, 0);

        var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var data = image.LockBits(new Gdi.Rectangle(0, 0, image.Width, image.Height),
            Gdi.Imaging.ImageLockMode.ReadOnly, Gdi.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[image.Width * 4];
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                Marshal.Copy(row, 0, bitmap.GetPixels() + y * bitmap.RowBytes, row.Length);
            }
        }
        finally
        {
            image.UnlockBits(data);
        }
        return (bitmap, orientation);
    }

    private static SKBitmap Rotate(SKBitmap source, int degrees)
    {
        bool swap = degrees is 90 or 270;
        var target = new SKBitmap(new SKImageInfo(
            swap ? source.Height : source.Width,
            swap ? source.Width : source.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        using var canvas = new SKCanvas(target);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.Translate(target.Width / 2f, target.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0, paint);
        return target;
    }

    /// <summary>
    /// A kép, a rá csempézett vízjel és a fölé kerülő logó összerenderelése.
    /// A vízjel és a logó is elhagyható (null).
    /// </summary>
    public static SKBitmap Render(SKBitmap source, WatermarkPreset? watermark,
        SKBitmap? logo = null, LogoPlacement? placement = null, double logoOpacity = 100)
    {
        var target = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

        using var canvas = new SKCanvas(target);
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
            canvas.DrawBitmap(source, 0, 0, paint);

        if (watermark != null && !string.IsNullOrWhiteSpace(watermark.Text))
            DrawTiledText(canvas, source.Width, source.Height, watermark);

        if (logo != null && placement != null)
            DrawLogo(canvas, source.Width, source.Height, logo, placement, logoOpacity);

        return target;
    }

    private static void DrawLogo(SKCanvas canvas, float width, float height,
        SKBitmap logo, LogoPlacement placement, double opacity)
    {
        float logoWidth = (float)(placement.Width * width);
        float logoHeight = logoWidth * logo.Height / logo.Width;
        if (logoWidth < 1 || logoHeight < 1)
            return;

        var dest = SKRect.Create((float)(placement.X * width), (float)(placement.Y * height), logoWidth, logoHeight);
        byte alpha = (byte)Math.Clamp(opacity * 255 / 100.0, 0, 255);

        using var image = SKImage.FromBitmap(logo);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha), IsAntialias = true };
        var sampling = logoWidth < logo.Width
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
        canvas.DrawImage(image, dest, sampling, paint);
    }

    private static void DrawTiledText(SKCanvas canvas, float width, float height, WatermarkPreset preset)
    {
        float fontSize = (float)Math.Max(6, width * preset.FontSizePercent / 100.0);

        using var typeface = SKTypeface.FromFamilyName(
            preset.FontFamily,
            preset.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            preset.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;

        if (!Avalonia.Media.Color.TryParse(preset.ColorHex, out var color))
            color = Avalonia.Media.Colors.Gray;

        byte alpha = (byte)Math.Clamp(preset.Opacity * 255 / 100.0, 0, 255);
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, alpha), IsAntialias = true };

        // A szöveget futamokra bontjuk, hogy a betűtípusból hiányzó karakterek
        // (pl. emoji) egy helyettesítő betűtípussal jelenjenek meg.
        var fonts = new List<SKFont>();
        var blobs = new List<(SKTextBlob Blob, float X)>();
        try
        {
            float textWidth = 0;
            foreach (var (face, run) in SplitRuns(preset.Text, typeface))
            {
                var font = new SKFont(face, fontSize)
                {
                    Subpixel = true,
                    Edging = SKFontEdging.Antialias,
                    // Ha a családnak nincs valódi félkövér/dőlt változata, szimuláljuk.
                    Embolden = preset.Bold && face.FontWeight < (int)SKFontStyleWeight.SemiBold,
                    SkewX = preset.Italic && !face.IsItalic ? -0.25f : 0,
                };
                fonts.Add(font);

                var blob = SKTextBlob.Create(run, font);
                if (blob != null)
                    blobs.Add((blob, textWidth));
                textWidth += font.MeasureText(run);
            }

            var metrics = fonts[0].Metrics;
            float textHeight = metrics.Descent - metrics.Ascent + metrics.Leading;

            double stepX = textWidth + fontSize * preset.HorizontalSpacing;
            double stepY = textHeight + fontSize * preset.VerticalSpacing;
            if (stepX <= 1 || stepY <= 1)
                return;

            double cx = width / 2;
            double cy = height / 2;
            double diagonal = Math.Sqrt(width * width + height * height);

            // Negatív szög = jobbra felfelé emelkedő szöveg (mint a mintaképen).
            canvas.Save();
            canvas.RotateDegrees((float)-preset.Angle, (float)cx, (float)cy);

            int row = 0;
            for (double y = cy - diagonal; y < cy + diagonal; y += stepY, row++)
            {
                double offset = preset.Stagger && row % 2 == 1 ? stepX / 2 : 0;
                for (double x = cx - diagonal - offset; x < cx + diagonal; x += stepX)
                {
                    foreach (var (blob, blobX) in blobs)
                        canvas.DrawText(blob, (float)x + blobX, (float)y - metrics.Ascent, paint);
                }
            }

            canvas.Restore();
        }
        finally
        {
            foreach (var (blob, _) in blobs)
                blob.Dispose();
            foreach (var font in fonts)
                font.Dispose();
        }
    }

    private static List<(SKTypeface Face, string Text)> SplitRuns(string text, SKTypeface primary)
    {
        var runs = new List<(SKTypeface Face, string Text)>();
        var current = primary;
        var buffer = new System.Text.StringBuilder();

        foreach (var rune in text.EnumerateRunes())
        {
            var face = current;
            // Szóköz és láthatatlan módosító karakterek maradnak az aktuális futamban.
            if (!System.Text.Rune.IsWhiteSpace(rune) && rune.Value is not (0x200D or 0xFE0E or 0xFE0F))
            {
                face = primary.ContainsGlyph(rune.Value)
                    ? primary
                    : SKFontManager.Default.MatchCharacter(primary.FamilyName, primary.FontStyle, null, rune.Value) ?? primary;
            }

            if (face.FamilyName != current.FamilyName && buffer.Length > 0)
            {
                runs.Add((current, buffer.ToString()));
                buffer.Clear();
            }
            current = face;
            buffer.Append(rune.ToString());
        }

        if (buffer.Length > 0 || runs.Count == 0)
            runs.Add((current, buffer.ToString()));
        return runs;
    }

    /// <summary>Skia bitkép másolása az Avalonia felület számára.</summary>
    public static Bitmap ToAvaloniaBitmap(SKBitmap bitmap) =>
        new(PixelFormat.Bgra8888, AlphaFormat.Premul, bitmap.GetPixels(),
            new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), bitmap.RowBytes);

    /// <summary>Mentés a kért formátumban ("jpg", "png" vagy "orig").</summary>
    public static string Save(SKBitmap bitmap, string sourcePath, string targetDir, string format, int jpegQuality)
    {
        string sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
        string targetExt = format switch
        {
            "jpg" => ".jpg",
            "png" => ".png",
            _ => sourceExt is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" ? sourceExt : ".png",
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
        using var fs = File.Create(target);
        switch (targetExt)
        {
            case ".jpg" or ".jpeg":
                Encode(bitmap, SKEncodedImageFormat.Jpeg, jpegQuality, fs);
                break;
            case ".bmp":
                EncodeWithGdi(bitmap, Gdi.Imaging.ImageFormat.Bmp, fs);
                break;
            case ".tif" or ".tiff":
                EncodeWithGdi(bitmap, Gdi.Imaging.ImageFormat.Tiff, fs);
                break;
            default:
                Encode(bitmap, SKEncodedImageFormat.Png, 100, fs);
                break;
        }
        return target;
    }

    private static void Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality, Stream stream)
    {
        using var data = bitmap.Encode(format, quality)
            ?? throw new InvalidOperationException($"Encoding failed ({format}).");
        data.SaveTo(stream);
    }

    // A Skia nem tud BMP-t és TIFF-et írni, ezeket a GDI+ kódolja.
    private static void EncodeWithGdi(SKBitmap bitmap, Gdi.Imaging.ImageFormat format, Stream stream)
    {
        using var image = new Gdi.Bitmap(bitmap.Width, bitmap.Height, bitmap.RowBytes,
            Gdi.Imaging.PixelFormat.Format32bppPArgb, bitmap.GetPixels());
        image.Save(stream, format);
    }

    private static bool IsSamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
