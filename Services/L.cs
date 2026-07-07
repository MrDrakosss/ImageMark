using System.Windows;

namespace ImageMark.Services;

/// <summary>Lokalizált szövegek lekérése kódból.</summary>
public static class L
{
    public static string T(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string F(string key, params object[] args) =>
        string.Format(T(key), args);
}
