#if DEBUG
using PerceptoX.Presentation.Services;

namespace PerceptoX.WinUI.Services;

/// <summary>Synthetic display-only fixtures; no scans or writes to user image folders.</summary>
internal sealed class DesignGroupingWorkflow : ISimilarityGroupingWorkflow
{
    public Task<GroupingAnalysisResult> AnalyzeGroupsAsync(GroupingAnalysisRequest request, CancellationToken token)
        => Task.FromResult(new GroupingAnalysisResult("design-only", 3000, 1000, 3000, 97, 2, 0, []));
    public Task<IReadOnlyList<GroupedImageItem>> LoadGroupPageAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
        int offset, int? groupNumber, CancellationToken token)
    {
        List<GroupedImageItem> rows = [];
        string graphics = Path.Combine(AppContext.BaseDirectory, "graphics", "header-illustration.png");
        for (int i = offset; i < (groupNumber is null ? Math.Min(offset + 50, 1000) : 3); i++)
        {
            int group = groupNumber ?? i + 1;
            bool representative = groupNumber is null || i == 0;
            string name = representative ? "Original_" + group.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".jpg" : "Variant_" + i + ".jpg";
            bool binary = i == 1 && !representative;
            rows.Add(new(group, group * 10 + (representative ? 0 : i), representative, representative || binary ? 100 : 98.44,
                binary, "D:\\Photos\\" + name, representative || binary ? 3840 : 1920, representative || binary ? 2160 : 1080,
                representative || binary ? 4_200_000 : 900_000, 0, new Uri(graphics), 3));
        }
        return Task.FromResult<IReadOnlyList<GroupedImageItem>>(rows);
    }
    public Task<CopyOutcome> CopyGroupedAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
        GroupingSelection selection, string destination, CancellationToken token) => throw new NotSupportedException("Design preview only.");
    public Task<string> ExportGroupsAsync(WorkspaceConfiguration workspace, GroupingAnalysisResult run,
        GroupingSelection selection, string destination, bool html, CancellationToken token) => throw new NotSupportedException("Design preview only.");
}
#endif
