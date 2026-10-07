namespace PerceptoX.Presentation.Localization;

/// <summary>UI-only text catalog. Loaded in memory; switching never reads per-image data.</summary>
public static class UiText
{
    private static readonly LanguageCatalog Romanian = LoadBuiltIn("ro");
    private static volatile LanguageCatalog _current = Romanian;
    public static string Language => _current.Language;
    public static event EventHandler? Changed;
    public static void Initialize(LanguageCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (ReferenceEquals(_current, catalog)) return;
        _current = catalog;
        Changed?.Invoke(null, EventArgs.Empty);
    }
    public static LanguageCatalog LoadBuiltIn(string code)
    {
        using Stream stream = typeof(UiText).Assembly.GetManifestResourceStream("PerceptoX.Languages." + code + ".json")
            ?? throw new InvalidDataException("Built-in language unavailable.");
        return LanguageCatalog.Parse(stream);
    }
    public static string T(string key, params object?[] values) => _current.Format(key, Romanian, values);
}
