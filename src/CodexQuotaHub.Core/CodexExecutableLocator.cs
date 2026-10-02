namespace CodexQuotaHub.Core;

public static class CodexExecutableLocator
{
    public static string Resolve(string requestedExecutable = "codex", string? localAppDataDirectory = null)
    {
        // A user-supplied path always wins. Only the default command needs discovery.
        if (!string.Equals(requestedExecutable, "codex", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(requestedExecutable, "codex.exe", StringComparison.OrdinalIgnoreCase))
            return requestedExecutable;

        var localAppData = localAppDataDirectory ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            return requestedExecutable;

        var binDirectory = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        try
        {
            if (!Directory.Exists(binDirectory))
                return requestedExecutable;

            var candidates = new[] { Path.Combine(binDirectory, "codex.exe") }
                .Concat(Directory.EnumerateDirectories(binDirectory)
                    .Select(directory => Path.Combine(directory, "codex.exe")))
                .Where(File.Exists)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.FullName, StringComparer.OrdinalIgnoreCase);

            return candidates.FirstOrDefault()?.FullName ?? requestedExecutable;
        }
        catch (IOException)
        {
            return requestedExecutable;
        }
        catch (UnauthorizedAccessException)
        {
            return requestedExecutable;
        }
    }
}
