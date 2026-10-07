using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PerceptoX.Infrastructure.Maintenance;

namespace PerceptoX.WinUI.Services;

/// <summary>App-only theme. Does not change Windows settings or reload the engine.</summary>
internal static class ThemeManager
{
    private static readonly string PreferencePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PerceptoX", "theme.json");
    internal static ElementTheme Current { get; private set; } = ElementTheme.Light;
    internal static string Diagnostic { get; private set; } = "";
    internal static event EventHandler? Changed;

    internal static void Initialize()
    {
        try
        {
            ManagedPaths.RejectLinks(PreferencePath);
            if (!File.Exists(PreferencePath)) return;
            if (new FileInfo(PreferencePath).Length > 4096) throw new InvalidDataException("Theme preference too large.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(PreferencePath));
            Current = document.RootElement.GetProperty("theme").GetString() switch
            { "dark" => ElementTheme.Dark, "light" => ElementTheme.Light, _ => throw new InvalidDataException("Unknown theme.") };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        { Diagnostic = "Theme.PreferenceFailure"; Current = ElementTheme.Light; }
    }

    internal static void Apply(ElementTheme theme)
    {
        if (theme is not (ElementTheme.Light or ElementTheme.Dark)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (Current == theme) return;
        Current = theme;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static void SaveAndApply(ElementTheme theme)
    {
        if (theme is not (ElementTheme.Light or ElementTheme.Dark)) throw new ArgumentOutOfRangeException(nameof(theme));
        ManagedPaths.RejectLinks(PreferencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
        string temporary = PreferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, new { theme = theme == ElementTheme.Dark ? "dark" : "light" }); output.Flush(flushToDisk: true); }
            ManagedPaths.RejectLinks(PreferencePath);
            File.Move(temporary, PreferencePath, overwrite: true);
        }
        finally { File.Delete(temporary); }
        Apply(theme);
    }

    // Programmatically created controls cannot use XAML ThemeResource syntax.
    internal static Brush Brush(string key)
    {
        ResourceDictionary palette = (ResourceDictionary)Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries.Last().ThemeDictionaries[Current.ToString()];
        return (Brush)palette[key];
    }
}
