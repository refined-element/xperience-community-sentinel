using System.Diagnostics;

namespace XperienceCommunity.Sentinel.Cloning;

/// <summary>
/// Shallow-clones a repository into a scratch directory. Shells out to the system <c>git</c>
/// so the user's existing credential helpers (gh, SSH keys, credential manager) just work.
/// </summary>
public sealed class GitCloner
{
    public async Task<CloneResult> CloneAsync(string repoUrl, string? reference, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(Path.GetTempPath(), "sentinel-clone-" + Guid.NewGuid().ToString("N"));

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("clone");
        psi.ArgumentList.Add("--depth");
        psi.ArgumentList.Add("1");
        if (!string.IsNullOrWhiteSpace(reference))
        {
            psi.ArgumentList.Add("--branch");
            psi.ArgumentList.Add(reference);
        }
        psi.ArgumentList.Add("--single-branch");
        psi.ArgumentList.Add(repoUrl);
        psi.ArgumentList.Add(destination);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start `git`. Is it installed and on PATH?");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            SafeDelete(destination);
            return new CloneResult(Success: false, ClonePath: null, ErrorMessage: stderr.Trim());
        }

        return new CloneResult(Success: true, ClonePath: destination, ErrorMessage: null);
    }

    public static void SafeDelete(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            // `git` on Windows marks .git/objects/pack/*.idx as read-only; strip attributes before delete.
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort — clone directories sit under %TEMP% and will be cleaned by the OS eventually
        }
    }
}

public sealed record CloneResult(bool Success, string? ClonePath, string? ErrorMessage);
