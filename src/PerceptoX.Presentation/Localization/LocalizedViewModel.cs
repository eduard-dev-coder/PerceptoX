using CommunityToolkit.Mvvm.ComponentModel;

namespace PerceptoX.Presentation.Localization;

/// <summary>Retains only the latest renderer for each UI message, not a history of scan events.</summary>
public abstract class LocalizedViewModel : ObservableObject
{
    private readonly Dictionary<string, (Action<string> Set, Func<string> Render)> _messages = new(StringComparer.Ordinal);
    protected void SetLocalized(string property, Action<string> setter, Func<string> render)
    {
        _messages[property] = (setter, render);
        setter(render());
    }

    protected void AppendLocalized(string property, Action<string> setter, Func<string> suffix)
    {
        Func<string> prefix = _messages.TryGetValue(property, out var previous) ? previous.Render : () => "";
        SetLocalized(property, setter, () => prefix() + suffix());
    }

    public virtual void RefreshLanguage()
    {
        foreach (var message in _messages.Values) message.Set(message.Render());
        OnPropertyChanged(string.Empty);
    }
}
