namespace XperienceCommunity.Sentinel.Tests.Support;

/// <summary>
/// Creates a scratch directory on disk that's cleaned up on dispose. Tests use it to stage
/// fake appsettings.json / Program.cs / .csproj files for checks that scan the source tree.
/// </summary>
public sealed class TempRepo : IDisposable
{
    public string Path { get; }

    public TempRepo()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sentinel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Write(string relative, string contents)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
