using System.Text.Json;
using PerceptoX.Infrastructure.Maintenance;
using PerceptoX.Presentation.Localization;

namespace PerceptoX.WinUI.Localization;

internal static class LanguageManager
{
    private static readonly string PreferencePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PerceptoX", "language.json");
    internal static IReadOnlyList<LanguageCatalog> Languages { get; private set; } = [];
    internal static string PendingLanguage { get; private set; } = "ro";
    internal static string Diagnostic { get; private set; } = "";

    internal static void Initialize(string? languageOverride = null)
    {
        Dictionary<string, LanguageCatalog> languages = new(StringComparer.OrdinalIgnoreCase)
        { ["ro"] = UiText.LoadBuiltIn("ro"), ["en"] = UiText.LoadBuiltIn("en") };
        string directory = Path.Combine(AppContext.BaseDirectory, "languages");
        try
        {
            ManagedPaths.RejectLinks(directory);
            if (Directory.Exists(directory))
                foreach (string file in Directory.EnumerateFiles(directory, "*.json").Take(100))
                    try
                    {
                        ManagedPaths.RejectLinks(file);
                        if (new FileInfo(file).Length > 2_000_000) throw new InvalidDataException("Language pack too large.");
                        using FileStream stream = File.OpenRead(file);
                        LanguageCatalog catalog = LanguageCatalog.Parse(stream);
                        languages[catalog.Language] = catalog;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException)
                    { Diagnostic = "Language.InvalidPack"; }
            ManagedPaths.RejectLinks(PreferencePath);
            if (File.Exists(PreferencePath))
            {
                if (new FileInfo(PreferencePath).Length > 4096) throw new InvalidDataException("Language preference too large.");
                using JsonDocument preference = JsonDocument.Parse(File.ReadAllText(PreferencePath));
                string? requested = preference.RootElement.GetProperty("language").GetString();
                if (requested is not null && languages.ContainsKey(requested)) PendingLanguage = requested;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        { Diagnostic = "Language.PreferenceFailure"; }
        Languages = languages.Values.OrderBy(catalog => catalog.DisplayName, StringComparer.Ordinal).ToArray();
        if (languageOverride is not null && languages.ContainsKey(languageOverride)) PendingLanguage = languageOverride;
        UiText.Initialize(languages[PendingLanguage]);
        // UiText formats with the selected culture. Do not alter engine/cache/export culture globally.
    }

    internal static void Save(string code)
    {
        if (!Languages.Any(catalog => catalog.Language == code)) throw new ArgumentException("Unknown language.", nameof(code));
        ManagedPaths.RejectLinks(PreferencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
        string temporary = PreferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, new { language = code }); output.Flush(flushToDisk: true); }
            ManagedPaths.RejectLinks(PreferencePath);
            File.Move(temporary, PreferencePath, overwrite: true);
            PendingLanguage = code;
        }
        finally { File.Delete(temporary); }
        Apply(code);
    }

    internal static void Apply(string code)
    {
        LanguageCatalog catalog = Languages.FirstOrDefault(catalog => catalog.Language == code) ?? throw new ArgumentException("Unknown language.", nameof(code));
        PendingLanguage = code;
        UiText.Initialize(catalog);
    }
}
