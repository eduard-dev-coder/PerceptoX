using System.Text;
using PerceptoX.Presentation.Localization;

namespace PerceptoX.Presentation.Tests;

public sealed class LanguageCatalogTests
{
    private static LanguageCatalog Parse(string json)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
        return LanguageCatalog.Parse(stream);
    }

    [Fact]
    public void MissingKeyFallsBackToRomanianThenToKey()
    {
        LanguageCatalog ro = new("ro", "Română", new Dictionary<string, string> { ["Hello"] = "Salut" });
        LanguageCatalog en = new("en", "English", new Dictionary<string, string>());
        Assert.Equal("Salut", en.Get("Hello", ro));
        Assert.Equal("Unknown", en.Get("Unknown", ro));
    }

    [Fact]
    public void ParametersAndSelectedCultureArePreserved()
    {
        LanguageCatalog en = new("en", "English", new Dictionary<string, string> { ["Progress"] = "{0}/{1} processed · {2:F2} MB" });
        Assert.Equal("3/8 processed · 1.25 MB", en.Format("Progress", en, 3, 8, 1.25));
    }

    [Fact]
    public void BrokenCustomParameterReferenceFallsBackWithoutCrashing()
    {
        LanguageCatalog ro = new("ro", "Română", new Dictionary<string, string> { ["Count"] = "{0} imagini" });
        LanguageCatalog custom = new("fr", "Français", new Dictionary<string, string> { ["Count"] = "{7} images" });
        Assert.Equal("3 imagini", custom.Format("Count", ro, 3));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"language\":\"en\",\"displayName\":\"English\",\"strings\":{}}")]
    [InlineData("{\"schemaVersion\":1.5,\"language\":\"en\",\"displayName\":\"English\",\"strings\":{}}")]
    [InlineData("{\"schemaVersion\":\"1\",\"language\":\"en\",\"displayName\":\"English\",\"strings\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"language\":\"../en\",\"displayName\":\"English\",\"strings\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"language\":\"en\",\"displayName\":\"\",\"strings\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"language\":\"en\",\"displayName\":\"English\",\"strings\":{\"X\":\"a\",\"X\":\"b\"}}")]
    [InlineData("{\"schemaVersion\":1,\"language\":\"en\",\"displayName\":\"English\",\"strings\":{\"X\":\"{broken\"}}")]
    [InlineData("{\"schemaVersion\":1,\"language\":\"en\",\"displayName\":\"English\",\"strings\":{\"X\":7}}")]
    public void InvalidSchemaEntryOrPlaceholdersAreRejected(string json) => Assert.Throws<InvalidDataException>(() => Parse(json));

    [Fact]
    public void BuiltInEnglishHasExactlyTheRomanianKeysAndArgumentCounts()
    {
        LanguageCatalog ro = UiText.LoadBuiltIn("ro"), en = UiText.LoadBuiltIn("en");
        Assert.Equal(ro.Strings.Keys.Order(), en.Strings.Keys.Order());
        foreach ((string key, string template) in ro.Strings)
            Assert.Equal(System.Text.CompositeFormat.Parse(template).MinimumArgumentCount,
                System.Text.CompositeFormat.Parse(en.Strings[key]).MinimumArgumentCount);
    }

    [Fact]
    public void ThirdLanguageNeedsNoApplicationCodeChanges()
    {
        LanguageCatalog french = Parse("""{"schemaVersion":1,"language":"fr","displayName":"Français","strings":{"Hello":"Bonjour {0}"}}""");
        Assert.Equal("Bonjour PerceptoX", french.Format("Hello", french, "PerceptoX"));
    }
}
