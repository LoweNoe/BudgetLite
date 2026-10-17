namespace BudgetApp.Data;

public static class AtomicFile
{
    // Write the full payload to a sibling .tmp file, flush it to disk, then swap it in with a
    // rename. A crash at any point leaves either the old file or the new one, never half of each.
    public static async Task WriteAsync(string path, Func<Stream, Task> write, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var tempPath = path + ".tmp";

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await write(stream);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
