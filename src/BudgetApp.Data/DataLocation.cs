namespace BudgetApp.Data;

// The data folder can live anywhere (a synced drive, a USB stick). Its location is kept in a tiny
// pointer file inside the default folder, because settings.json itself moves with the data.
public static class DataLocation
{
    private static readonly string[] DataFiles =
        ["transactions.json", "categories.json", "rules.json", "accounts.json", "settings.json", "budgets.json", "bank-tips.json"];

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BudgetApp");

    private static string PointerFile => Path.Combine(DefaultRoot, "data-location.txt");

    public static async Task<DataPaths> ResolveAsync()
    {
        if (File.Exists(PointerFile))
        {
            var root = (await File.ReadAllTextAsync(PointerFile)).Trim();
            if (root.Length > 0 && Directory.Exists(root))
                return new DataPaths(root);
        }

        return new DataPaths(DefaultRoot);
    }

    public static bool HasData(string root) => DataFiles.Any(f => File.Exists(Path.Combine(root, f)));

    // Copies the current files across (unless the target already holds data the user wants to
    // switch to) and points future launches at the new folder.
    public static async Task MoveAsync(DataPaths current, string newRoot, bool copyFiles, CancellationToken ct = default)
    {
        var target = Path.GetFullPath(newRoot);
        Directory.CreateDirectory(target);

        if (copyFiles && !string.Equals(Path.GetFullPath(current.Root), target, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var name in DataFiles)
            {
                var source = Path.Combine(current.Root, name);
                if (!File.Exists(source))
                    continue;

                await using var input = File.OpenRead(source);
                await AtomicFile.WriteAsync(Path.Combine(target, name), output => input.CopyToAsync(output, ct), ct);
            }
        }

        Directory.CreateDirectory(DefaultRoot);
        if (string.Equals(target, Path.GetFullPath(DefaultRoot), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(PointerFile);
        }
        else
        {
            await File.WriteAllTextAsync(PointerFile, target, ct);
        }
    }

    internal static IReadOnlyList<string> Files => DataFiles;
}
