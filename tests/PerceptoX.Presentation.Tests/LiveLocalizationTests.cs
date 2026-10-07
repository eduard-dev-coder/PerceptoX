using System.Globalization;
using PerceptoX.Presentation.Localization;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.Presentation.Tests;

[CollectionDefinition("Live language", DisableParallelization = true)]
public sealed class LiveLanguageTestsScope;

[Collection("Live language")]
public sealed class LiveLocalizationTests
{
    [Fact]
    public async Task GroupLabelsAndScoresSwitchWithoutReplacingSelectionOrItems()
    {
        // Test the live-bound row properties used by the new page, without touching global engine culture.
        var row = new GroupedImageViewModel(new(1, 2, false, 98.44, false, "D:\\photo.png", 200, 100, 1234, 0, null, 2)) { IsSelected = true };
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            UiText.Initialize(UiText.LoadBuiltIn("ro")); Assert.Equal("98,44%", row.Score);
            UiText.Initialize(UiText.LoadBuiltIn("en")); row.RefreshLanguage();
            Assert.Equal("Compare / Zoom", row.CompareLabel); Assert.Equal("98.44%", row.Score);
            Assert.True(row.IsSelected); Assert.Same(previous, CultureInfo.CurrentCulture);
            await Task.CompletedTask;
        }
        finally { UiText.Initialize(UiText.LoadBuiltIn("ro")); }
    }

    [Fact]
    public void CatalogChangeNotifiesOnceAndDoesNotAlterEngineCulture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        LanguageCatalog en = UiText.LoadBuiltIn("en");
        int calls = 0;
        void Changed(object? sender, EventArgs args) => calls++;
        UiText.Changed += Changed;
        try
        {
            UiText.Initialize(en); UiText.Initialize(en);
            Assert.Equal(1, calls);
            Assert.Equal("Scan", UiText.T("BatchMatchPage.Text011"));
            Assert.Same(before, CultureInfo.CurrentCulture);
        }
        finally { UiText.Changed -= Changed; UiText.Initialize(UiText.LoadBuiltIn("ro")); }
    }

    [Fact]
    public void LatestRendererReplacesPreviousMessageAndKeepsRawDetail()
    {
        MessageModel model = new();
        model.Set(() => UiText.T("SearchViewModel.Text008", 42));
        model.Set(() => UiText.T("Language.SaveFailure", "raw-file.txt"));
        try
        {
            UiText.Initialize(UiText.LoadBuiltIn("en")); model.RefreshLanguage();
            Assert.Equal(UiText.T("Language.SaveFailure", "raw-file.txt"), model.Text);
            Assert.DoesNotContain("42", model.Text);
            Assert.Contains("raw-file.txt", model.Text);
        }
        finally { UiText.Initialize(UiText.LoadBuiltIn("ro")); }
    }

    [Fact]
    public void AppendedFragmentsReformatTogetherAfterLanguageChange()
    {
        MessageModel model = new();
        model.Set(() => UiText.T("BatchMatchViewModel.Text016", 3, 1));
        model.Append(() => UiText.T("BatchMatchViewModel.Text017"));
        try
        {
            UiText.Initialize(UiText.LoadBuiltIn("en")); model.RefreshLanguage();
            Assert.Equal(UiText.T("BatchMatchViewModel.Text016", 3, 1) + UiText.T("BatchMatchViewModel.Text017"), model.Text);
        }
        finally { UiText.Initialize(UiText.LoadBuiltIn("ro")); }
    }

    [Fact]
    public async Task SwitchingLanguagePreservesResultsSelectionsPathsAndThresholds()
    {
        FakeWorkflow workflow = new();
        using ShellViewModel shell = new(workflow, new FakePicker());
        shell.Workspace.LibraryRoot = "D:\\Originals";
        shell.BatchMatch.QueryRoot = "D:\\References";
        await shell.BatchMatch.MatchCommand.ExecuteAsync(null);
        shell.BatchMatch.SelectBestCommand.Execute(null);
        BatchResultViewModel row = shell.BatchMatch.Results[0];
        try
        {
            UiText.Initialize(UiText.LoadBuiltIn("en")); shell.RefreshLanguage();
            Assert.Same(row, shell.BatchMatch.Results[0]);
            Assert.True(row.Candidates[0].IsSelected);
            Assert.Equal(1, shell.BatchMatch.SelectedCount);
            Assert.Equal("D:\\References", shell.BatchMatch.QueryRoot);
            Assert.Equal("D:\\Originals", shell.Workspace.LibraryRoot);
            Assert.Equal(2, shell.Settings.MaxPerceptualDistance);
            Assert.Equal(1, workflow.Matches);
            Assert.Equal(UiText.T("IPerceptoXWorkflow.Text007"), row.StatusLabel);
            Assert.Contains("found", shell.BatchMatch.StatusMessage);
            UiText.Initialize(UiText.LoadBuiltIn("ro")); shell.RefreshLanguage();
            Assert.Equal("Găsită", row.StatusLabel);
            Assert.True(row.Candidates[0].IsSelected);
        }
        finally { UiText.Initialize(UiText.LoadBuiltIn("ro")); }
    }

    [Fact]
    public void PartialThirdLanguageStillFallsBackAfterLiveSwitch()
    {
        try
        {
            UiText.Initialize(new LanguageCatalog("fr", "Français", new Dictionary<string, string> { ["BatchMatchPage.Text011"] = "Analyser" }));
            Assert.Equal("Analyser", UiText.T("BatchMatchPage.Text011"));
            Assert.Equal("Aspectul aplicației", UiText.T("Theme.Title"));
        }
        finally { UiText.Initialize(UiText.LoadBuiltIn("ro")); }
    }

    private sealed class MessageModel : LocalizedViewModel
    {
        public string Text { get; private set; } = "";
        public void Set(Func<string> render) => SetLocalized(nameof(Text), value => Text = value, render);
        public void Append(Func<string> render) => AppendLocalized(nameof(Text), value => Text = value, render);
    }

    private sealed class FakePicker : IPathPickerService
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class FakeWorkflow : IPerceptoXWorkflow
    {
        public int Matches { get; private set; }
        public Task<IndexingSummary> IndexAsync(WorkspaceConfiguration workspace, CancellationToken cancellationToken) => Task.FromResult(new IndexingSummary(1, 1, 0, 1, 0, 0));
        public Task<IReadOnlyList<SimilarImageItem>> FindSimilarAsync(SimilarityQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SimilarImageItem>>([]);
        public Task<BatchMatchSummary> MatchFolderAsync(BatchMatchRequest request, CancellationToken cancellationToken)
        {
            Matches++;
            SimilarImageItem candidate = new(1, "D:\\Originals\\one.jpg", null, 98, 1, 1, null, 0);
            BatchMatchItem item = new("D:\\References\\one.png", BatchMatchStatus.Found, 1, candidate.FilePath, null, 98, 1, 1, 1, null, null) { Candidates = [candidate] };
            return Task.FromResult(new BatchMatchSummary(new(1, 1, 0, 1, 0, 0), 1, 1, 0, 0, 0, 0, [item]));
        }
    }
}
