#if DEBUG
namespace PerceptoX.WinUI.Services;

internal static class UxProfileOptions
{
    public const int ExpectedResultCount = 1000;

    public static string? ReportPath { get; } = Environment.GetCommandLineArgs()
        .Skip(1)
        .FirstOrDefault(argument => argument.StartsWith("--ux-report=", StringComparison.OrdinalIgnoreCase))?
        .Split('=', 2)[1] is { Length: > 0 } path
            ? Path.GetFullPath(path)
            : null;
}
#endif
