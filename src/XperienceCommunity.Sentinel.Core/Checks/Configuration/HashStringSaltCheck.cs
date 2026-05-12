using System.Text.Json;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Configuration;

/// <summary>
/// CFG001 — CMSHashStringSalt is either missing, or hard-coded in appsettings.json. Both are wrong.
/// The expected pattern is: key present in appsettings.json with an empty string value, actual GUID supplied
/// via User Secrets (local) or Azure App Settings / Key Vault (prod).
/// </summary>
public sealed class HashStringSaltCheck : ICheck
{
    public string RuleId => "CFG001";
    public string Title => "CMSHashStringSalt configuration";
    public string Category => "Configuration";
    public CheckKind Kind => CheckKind.Static;

    public Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();
        var appsettings = Path.Combine(context.RepoPath, "appsettings.json");
        if (!File.Exists(appsettings))
        {
            findings.Add(new Finding(
                RuleId, Title, Category,
                Severity.Info,
                "No appsettings.json found at the repo root — skipping CMSHashStringSalt check.",
                Location: appsettings));
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(appsettings));
        var root = doc.RootElement;

        if (!root.TryGetProperty("CMSHashStringSalt", out var saltElement))
        {
            findings.Add(new Finding(
                RuleId, Title, Category,
                Severity.Error,
                "CMSHashStringSalt is missing from appsettings.json. Xperience by Kentico will fail to start without it.",
                Location: appsettings,
                Remediation: "Add \"CMSHashStringSalt\": \"\" to appsettings.json, then set the actual GUID via user secrets locally and Key Vault in production."));
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var saltValue = saltElement.GetString();
        if (!string.IsNullOrEmpty(saltValue))
        {
            findings.Add(new Finding(
                RuleId, Title, Category,
                Severity.Warning,
                "CMSHashStringSalt has a hard-coded value in appsettings.json. Secrets committed to source control are a leak risk.",
                Location: appsettings,
                Remediation: "Replace the hard-coded value with an empty string and supply the real GUID via user secrets (dev) or Azure App Settings / Key Vault (prod)."));
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
