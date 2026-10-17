using System.IO.Compression;
using System.Text.Json;

namespace BudgetApp.Data;

public sealed class BackupService
{
    private readonly DataPaths _paths;

    public BackupService(DataPaths paths)
    {
        _paths = paths;
    }

    public string SafetyBackupFolder => Path.Combine(_paths.Root, "backups");

    public async Task CreateAsync(Stream output, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in DataLocation.Files)
            {
                var path = Path.Combine(_paths.Root, name);
                if (!File.Exists(path))
                    continue;

                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var entryStream = await entry.OpenAsync(ct);
                await using var file = File.OpenRead(path);
                await file.CopyToAsync(entryStream, ct);
            }
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(output, ct);
    }

    // Checks the archive first, keeps a safety copy of the current data, then swaps each file in
    // atomically. Unknown entries in the zip are ignored.
    public async Task<RestoreResult> RestoreAsync(Stream input, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (!DataLocation.Files.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                await using var stream = await entry.OpenAsync(ct);
                using var bytes = new MemoryStream();
                await stream.CopyToAsync(bytes, ct);
                contents[entry.Name] = bytes.ToArray();
            }
        }
        catch (InvalidDataException)
        {
            return RestoreResult.Fail("That file isn't a BudgetLite backup.");
        }

        if (!contents.ContainsKey("settings.json") && !contents.ContainsKey("transactions.json"))
            return RestoreResult.Fail("That zip doesn't contain any BudgetLite data.");

        foreach (var (name, bytes) in contents)
        {
            if (!LooksLikeVersionedJson(bytes))
                return RestoreResult.Fail($"{name} in the backup is damaged, so nothing was restored.");
        }

        Directory.CreateDirectory(SafetyBackupFolder);
        var safety = Path.Combine(SafetyBackupFolder, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        await using (var file = File.Create(safety))
            await CreateAsync(file, ct);

        foreach (var (name, bytes) in contents)
            await AtomicFile.WriteAsync(Path.Combine(_paths.Root, name), s => s.WriteAsync(bytes, ct).AsTask(), ct);

        return RestoreResult.Ok(safety);
    }

    private static bool LooksLikeVersionedJson(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("version", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record RestoreResult(bool Success, string? Error, string? SafetyBackupPath)
{
    public static RestoreResult Ok(string safetyBackup) => new(true, null, safetyBackup);
    public static RestoreResult Fail(string error) => new(false, error, null);
}
