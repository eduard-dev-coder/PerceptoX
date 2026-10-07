using System.Globalization;
using System.Text.Json;

namespace PerceptoX.Presentation.Localization;

public sealed record LanguageCatalog(string Language, string DisplayName, IReadOnlyDictionary<string, string> Strings)
{
    public static LanguageCatalog Parse(Stream stream)
    {
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
        JsonElement root = document.RootElement;
        JsonElement schema = root.GetProperty("schemaVersion");
        if (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version != 1)
            throw new InvalidDataException("Unsupported language schema.");
        string code = root.GetProperty("language").GetString() ?? "";
        if (code.Length is < 2 or > 20 || code.Any(character => !char.IsAsciiLetter(character) && character != '-'))
            throw new InvalidDataException("Invalid language code.");
        _ = CultureInfo.GetCultureInfo(code);
        string name = root.GetProperty("displayName").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidDataException("Invalid language name.");
        Dictionary<string, string> strings = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in root.GetProperty("strings").EnumerateObject())
        {
            if (entry.Name.Length is < 1 or > 160 || entry.Value.ValueKind != JsonValueKind.String || strings.Count >= 10000)
                throw new InvalidDataException("Invalid translation entry.");
            string value = entry.Value.GetString()!;
            if (value.Length > 8192 || !strings.TryAdd(entry.Name, value)) throw new InvalidDataException("Duplicate or oversized translation.");
            try { _ = System.Text.CompositeFormat.Parse(value); }
            catch (FormatException error) { throw new InvalidDataException("Invalid translation placeholders: " + entry.Name, error); }
        }
        return new(code, name, strings);
    }

    public string Get(string key, LanguageCatalog fallback) => Strings.TryGetValue(key, out string? text) ? text :
        fallback.Strings.TryGetValue(key, out text) ? text : key;

    public string Format(string key, LanguageCatalog fallback, params object?[] values)
    {
        string template = Get(key, fallback);
        try { return string.Format(CultureInfo.GetCultureInfo(Language), template, values); }
        catch (FormatException)
        {
            // A custom pack cannot crash a scan by referring to a non-existent argument.
            string original = fallback.Get(key, fallback);
            return string.Format(CultureInfo.GetCultureInfo(fallback.Language), original, values);
        }
    }
}
