namespace CodexQuotaHub.Core;

public static class ApplicationPaths
{
    public static string GetDefaultDatabasePath(string? localApplicationData = null)
    {
        localApplicationData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var currentPath = Path.Combine(localApplicationData, "CodexQuotaHub", "meter.db");
        // Reuse an existing database so the rename preserves snapshots, aliases, and settings.
        var legacyPath = Path.Combine(localApplicationData, "CodexMeter", "meter.db");
        return !File.Exists(currentPath) && File.Exists(legacyPath) ? legacyPath : currentPath;
    }
}
