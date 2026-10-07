using System.Text.Json;
using System.Security.Cryptography;
using PerceptoX.Application.Calibration;
using PerceptoX.Infrastructure.Maintenance;

namespace PerceptoX.Infrastructure.Calibration;

public sealed class JsonCalibrationStore(string path) : ICalibrationStore
{
    private string? _loadedDigest;
    private bool _loaded;
    public CalibrationState Load(ThresholdProfile preset)
    {
        _loaded = false;
        ManagedPaths.RejectLinks(path);
        if (!File.Exists(path)) { _loadedDigest = null; _loaded = true; return new(1, new ProfileSelection(preset), []); }
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Fișierul de calibrare depășește 16 MB.");
        byte[] bytes = File.ReadAllBytes(path);
        CalibrationState state = JsonSerializer.Deserialize<CalibrationState>(bytes)
            ?? throw new InvalidDataException("Calibrare invalidă.");
        if (state.SchemaVersion != 1 || state.Selection.Active.ProcessingProfileId != preset.ProcessingProfileId)
            throw new InvalidDataException("Profil de calibrare incompatibil; nu este aplicat.");
        _ = state.Selection.Active.ToSearchOptions();
        if (state.Samples is null || state.Samples.Count > 20_000) throw new InvalidDataException("Colecție de feedback invalidă.");
        foreach (FeedbackSample sample in state.Samples) sample.Validate();
        if (state.Selection.Previous is { } previous)
        {
            _ = previous.ToSearchOptions();
            if (previous.ProcessingProfileId != preset.ProcessingProfileId) throw new InvalidDataException("Profil anterior incompatibil.");
        }
        _loadedDigest = Convert.ToHexString(SHA256.HashData(bytes));
        _loaded = true;
        return state;
    }

    public void Save(CalibrationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ManagedPaths.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream lease = new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!_loaded) throw new InvalidOperationException("Starea de calibrare nu a fost încărcată valid; fișierul existent este păstrat.");
        if (File.Exists(path) && new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Fișierul de calibrare extern depășește limita.");
        string? current = File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
        if (current != _loadedDigest) throw new IOException("Calibrarea s-a modificat în altă instanță. Reporniți aplicația înainte de a salva feedback nou.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state);
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            ManagedPaths.RejectLinks(path);
            File.Move(temporary, path, overwrite: true);
            _loadedDigest = Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { File.Delete(temporary); }
    }
}
