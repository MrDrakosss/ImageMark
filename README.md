# ImageMark

**ImageMark** is a simple, fast watermarking tool for Windows. Add a repeating text watermark to one photo or a whole batch in just a few clicks — pick a saved preset, drop in your images, and apply.

It is designed for photographers, crafters, and small shops who need to protect product photos before publishing them online.

![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-blue)

## Features

### Tiled text watermark
The watermark text is repeated across the entire image in a diagonal, tiled pattern, so it cannot simply be cropped away. Everything about the pattern is adjustable:

- **Text** – any text, with full Unicode support (accents, symbols)
- **Font** – choose from all fonts installed on your system, with live font preview in the list; bold and italic styles supported
- **Size** – set as a percentage of the image width, so the watermark looks the same on small and large photos alike
- **Opacity** – from a barely visible 5% up to fully opaque
- **Angle** – from horizontal (0°) to vertical (90°) rising text
- **Color** – quick color swatches or any custom hex color code
- **Spacing** – independent horizontal and vertical gap control
- **Brick pattern** – optionally offset every second row for better coverage

### Live preview
Every change is shown immediately on the selected photo in the center panel, so you can fine-tune the watermark before touching any files.

### Presets
Save your favorite watermark configurations as named presets and reuse them any time:

- Create, rename, and delete presets — they are stored permanently on your machine
- **Export** presets to a file: pick exactly which presets to share (one, several, or all)
- **Import** presets from a file: imported presets are added alongside your existing ones, nothing gets overwritten

This makes it easy to keep separate watermarks for different brands or shops, and to share them between computers or with colleagues.

### Batch processing
- Add any number of images at once via the file picker, or simply **drag & drop** them from File Explorer
- Supported formats: JPEG, PNG, BMP, TIFF
- One click applies the selected watermark to every image in the list, with a progress bar
- Camera photos are handled correctly — EXIF orientation is respected, so nothing comes out rotated

### Safe output
- Your **original files are never overwritten**
- Choose an output folder, or leave it empty to save next to the originals
- If a file with the same name already exists, the result is saved with a `_watermark` suffix instead
- Output format: keep the original format, or convert to JPEG (with adjustable quality) or PNG
- After processing, open the output folder with one click

### Multilingual interface
The interface is available in **English**, **Hungarian (Magyar)**, and **German (Deutsch)**. Switch languages instantly from the selector in the bottom-left corner — your choice is remembered.

## Getting started

1. Run the installer (`ImageMark-Setup-x.x.x.exe`). It does not require administrator rights and includes everything the app needs — no additional downloads.
2. Start **ImageMark** from the Start menu or the desktop shortcut.
3. **Add images** on the left (button or drag & drop).
4. Adjust the **watermark settings** on the right — the preview updates as you type.
5. Press **Save** to store your settings as a preset for next time.
6. Optionally pick an **output folder** and format.
7. Click **Apply watermark to images** — done!

## Requirements

- Windows 10 or Windows 11 (64-bit)
- No other software required — the .NET runtime is bundled with the installer

---

## Building from source

Developer notes — only needed if you want to build the app yourself.

**Prerequisites:** [.NET SDK](https://dotnet.microsoft.com/download) 10 or newer.

```powershell
# Debug build + run
dotnet run

# Self-contained release build (no .NET required on target machines)
dotnet publish -c Release -r win-x64 --self-contained -o publish
```

### Building the installer

**Prerequisites:** [Inno Setup 6](https://jrsoftware.org/isinfo.php) (e.g. `winget install JRSoftware.InnoSetup`).

```powershell
# 1. Publish the app first (see above), then compile the installer:
ISCC.exe installer\ImageMark.iss
```

The installer is written to `dist\ImageMark-Setup-<version>.exe`. When releasing a new version, update the version number in both `ImageMark.csproj` and `installer\ImageMark.iss`.

### Project layout

| Path | Purpose |
|---|---|
| `MainWindow.xaml(.cs)` | Main window UI and logic |
| `Services/WatermarkRenderer.cs` | Image loading, watermark rendering, saving |
| `Services/PresetStore.cs` | Preset persistence (`%AppData%\ImageMark\presets.json`) |
| `Resources/Lang.*.xaml` | UI translations — copy one file to add a new language |
| `installer/ImageMark.iss` | Inno Setup installer script |
