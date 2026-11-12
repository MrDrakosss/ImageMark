using Avalonia;
using Avalonia.Controls;

namespace ImageMark.Services;

/// <summary>Lokalizált szövegek lekérése kódból.</summary>
public static class L
{
    public static string T(string key) =>
        Application.Current!.TryFindResource(key, out var value) && value is string text ? text : key;

    public static string F(string key, params object[] args) =>
        string.Format(T(key), args);
}
