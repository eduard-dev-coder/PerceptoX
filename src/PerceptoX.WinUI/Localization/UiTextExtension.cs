using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using PerceptoX.Presentation.Localization;

namespace PerceptoX.WinUI.Localization;

public sealed class UiTextExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    protected override object ProvideValue() => new Binding
    { Source = LiveUiText.For(Key), Path = new PropertyPath(nameof(LiveUiText.Text)), Mode = BindingMode.OneWay };
}

// One entry per catalog key, not one UiText.Changed subscription per control.
public sealed class LiveUiText : ObservableObject
{
    private static readonly Dictionary<string, LiveUiText> Entries = new(StringComparer.Ordinal);
    private readonly string _key;
    private LiveUiText(string key) => _key = key;
    public string Text => UiText.T(_key);
    static LiveUiText() => UiText.Changed += (_, _) =>
    {
        foreach (LiveUiText text in Entries.Values.ToArray()) text.OnPropertyChanged(nameof(Text));
    };
    internal static LiveUiText For(string key)
    {
        if (!Entries.TryGetValue(key, out LiveUiText? text)) Entries[key] = text = new(key);
        return text;
    }
}
