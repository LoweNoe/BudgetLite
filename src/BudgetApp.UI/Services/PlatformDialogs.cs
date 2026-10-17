using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using BudgetApp.Core.Import;

namespace BudgetApp.UI.Services;

public interface IPlatformDialogs
{
    Task<IReadOnlyList<StatementFile>> PickStatementsAsync();
    Task<Stream?> SaveFileAsync(string title, string suggestedName, string typeName, string extension);
    Task<Stream?> OpenFileAsync(string title, string typeName, string extension);
    Task<string?> PickFolderAsync(string title);
    Task OpenFolderAsync(string path);
}

public sealed class PlatformDialogs : IPlatformDialogs
{
    private static readonly FilePickerFileType Statements = new("Bank statements (CSV or PDF)")
    {
        Patterns = ["*.csv", "*.txt", "*.pdf"],
        MimeTypes = ["text/csv", "text/plain", "application/pdf"],
    };

    public static async Task<StatementFile> ReadStatementAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return new StatementFile(file.Name, buffer.ToArray());
    }

    public async Task<IReadOnlyList<StatementFile>> PickStatementsAsync()
    {
        if (TopLevel is not { } top)
            return [];

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose your bank statements",
            AllowMultiple = true,
            FileTypeFilter = [Statements, FilePickerFileTypes.All],
        });

        var statements = new List<StatementFile>();
        foreach (var file in files)
            statements.Add(await ReadStatementAsync(file));
        return statements;
    }

    public async Task<Stream?> SaveFileAsync(string title, string suggestedName, string typeName, string extension)
    {
        if (TopLevel is not { } top)
            return null;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [$"*.{extension}"] }],
        });

        return file is null ? null : await file.OpenWriteAsync();
    }

    public async Task<Stream?> OpenFileAsync(string title, string typeName, string extension)
    {
        if (TopLevel is not { } top)
            return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = [$"*.{extension}"] }],
        });

        return files.Count == 0 ? null : await files[0].OpenReadAsync();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (TopLevel is not { } top)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task OpenFolderAsync(string path)
    {
        Directory.CreateDirectory(path);
        if (TopLevel is { } top)
            await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    private static TopLevel? TopLevel =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
