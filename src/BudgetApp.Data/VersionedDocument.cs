namespace BudgetApp.Data;

public sealed class VersionedDocument<T>
{
    public int Version { get; set; }
    public T? Data { get; set; }
}

internal static class Migrations
{
    // Hook for future schema changes: step the document up one version at a time.
    public static VersionedDocument<T> Upgrade<T>(VersionedDocument<T> document)
    {
        if (document.Version > JsonDataStore.CurrentFileVersion)
            throw new NotSupportedException(
                $"This data was written by a newer version of the app (file version {document.Version}).");

        document.Version = JsonDataStore.CurrentFileVersion;
        return document;
    }
}
