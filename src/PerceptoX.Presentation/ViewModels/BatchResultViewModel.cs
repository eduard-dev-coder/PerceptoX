using CommunityToolkit.Mvvm.ComponentModel;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.Localization;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class MatchCandidateViewModel(SimilarImageItem image) : ObservableObject
{
    public SimilarImageItem Image { get; } = image;
    public string QueryPath { get; init; } = "";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed class BatchResultViewModel(BatchMatchItem item) : LocalizedViewModel
{
    public BatchMatchItem Item { get; } = item;
    public string StatusLabel => Item.StatusLabel;
    public string QueryPreviewPlaceholder => Item.QueryPreviewPlaceholder;
    public IReadOnlyList<MatchCandidateViewModel> Candidates { get; } = item.Candidates
        .Select(candidate => new MatchCandidateViewModel(candidate) { QueryPath = item.QueryPath }).ToArray();
}
