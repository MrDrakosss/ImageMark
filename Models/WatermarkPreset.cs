namespace ImageMark.Models;

public class WatermarkPreset
{
    public string Name { get; set; } = "Új preset";
    public string Text { get; set; } = "SAMPLE TEXT";
    public string FontFamily { get; set; } = "Segoe UI";
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>Betűméret a kép szélességének százalékában.</summary>
    public double FontSizePercent { get; set; } = 5;

    public string ColorHex { get; set; } = "#9E9E9E";

    /// <summary>Átlátszatlanság százalékban (0–100).</summary>
    public double Opacity { get; set; } = 45;

    /// <summary>Dőlésszög fokban (pozitív = jobbra felfelé emelkedő szöveg).</summary>
    public double Angle { get; set; } = 30;

    /// <summary>Vízszintes térköz a betűméret szorzataként.</summary>
    public double HorizontalSpacing { get; set; } = 1.5;

    /// <summary>Függőleges térköz a betűméret szorzataként.</summary>
    public double VerticalSpacing { get; set; } = 2.5;

    /// <summary>Minden második sor félrácsnyi eltolása (téglafal-minta).</summary>
    public bool Stagger { get; set; } = true;

    public WatermarkPreset Clone() => (WatermarkPreset)MemberwiseClone();
}
