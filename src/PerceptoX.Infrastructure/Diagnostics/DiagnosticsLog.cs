using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Json;

namespace PerceptoX.Infrastructure.Diagnostics;

public static class DiagnosticsLog
{
    public static ILoggerFactory CreateFactory(string directory)
    {
        Directory.CreateDirectory(directory);
        var logger = new LoggerConfiguration().MinimumLevel.Information()
            .WriteTo.File(new JsonFormatter(), Path.Combine(directory, "perceptox-.jsonl"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true, retainedFileCountLimit: 14,
                buffered: true, flushToDiskInterval: TimeSpan.FromSeconds(2))
            .CreateLogger();
        return new SerilogLoggerFactory(logger, dispose: true);
    }
}
