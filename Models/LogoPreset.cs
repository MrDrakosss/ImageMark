namespace ImageMark.Models;

public class LogoPreset
{
    public string Name { get; set; } = "Új preset";

    /// <summary>A logó másolatának fájlneve a program logómappájában (üres = nincs kép).</summary>
    public string ImageFile { get; set; } = "";

    /// <summary>Az eredetileg kiválasztott fájl neve, csak megjelenítéshez.</summary>
    public string ImageName { get; set; } = "";

    /// <summary>A logó szélessége a kép szélességének százalékában.</summary>
    public double SizePercent { get; set; } = 20;

    /// <summary>Átlátszatlanság százalékban (0–100).</summary>
    public double Opacity { get; set; } = 100;

    /// <summary>Alaphelyzet egy 3×3-as rácsban: sor × 3 + oszlop (8 = jobb alsó sarok).</summary>
    public int Anchor { get; set; } = 8;

    /// <summary>Távolság a kép szélétől a kép szélességének százalékában.</summary>
    public double MarginPercent { get; set; } = 3;

    public LogoPreset Clone() => (LogoPreset)MemberwiseClone();
}

/// <summary>
/// A logó helye egy képen, a kép méretéhez viszonyítva: a bal felső sarok (0–1)
/// és a szélesség a kép szélességének arányában. A magasság a logó képarányából adódik.
/// </summary>
public record LogoPlacement(double X, double Y, double Width)
{
    /// <summary>A preset szerinti alaphelyzet egy adott méretű képen.</summary>
    /// <param name="logoAspect">A logó magasság/szélesség aránya.</param>
    public static LogoPlacement FromPreset(LogoPreset preset, double imageWidth, double imageHeight, double logoAspect)
    {
        double width = preset.SizePercent / 100;
        double height = width * imageWidth * logoAspect / imageHeight;
        double marginX = preset.MarginPercent / 100;
        double marginY = marginX * imageWidth / imageHeight;

        int anchor = Math.Clamp(preset.Anchor, 0, 8);
        double x = (anchor % 3) switch
        {
            0 => marginX,
            1 => (1 - width) / 2,
            _ => 1 - width - marginX,
        };
        double y = (anchor / 3) switch
        {
            0 => marginY,
            1 => (1 - height) / 2,
            _ => 1 - height - marginY,
        };
        return new LogoPlacement(x, y, width);
    }
}
