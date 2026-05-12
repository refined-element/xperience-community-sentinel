using System.Text.Json;
using XperienceCommunity.Sentinel.Core;

namespace XperienceCommunity.Sentinel.Checks.Configuration;

/// <summary>
/// CFG003 — Secrets committed to appsettings.json are a leak waiting to happen. Flag any string value
/// whose key looks sensitive (Password, Secret, Key, Token, ApiKey, ClientSecret) unless the value is
/// empty, a placeholder, or a Key Vault reference.
/// </summary>
public sealed class PlaintextSecretsCheck : ICheck
{
    private static readonly string[] SensitiveKeyMarkers =
    [
        "password", "secret", "apikey", "api_key", "token", "clientsecret", "accesskey", "privatekey",
    ];

    private static readonly string[] AllowedValuePrefixes =
    [
        "@Microsoft.KeyVault(",
        "$(",       // MSBuild variable
        "${",       // environment-variable style
    ];

    public string RuleId => "CFG003";
    public string Title => "Plaintext secrets in appsettings.json";
    public string Category => "Configuration";
    public CheckKind Kind => CheckKind.Static;

    public Task<IReadOnlyList<Finding>> RunAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();
        var appsettings = Path.Combine(context.RepoPath, "appsettings.json");
        if (!File.Exists(appsettings))
        {
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(appsettings));
        Walk(doc.RootElement, path: "", findings, appsettings);

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private void Walk(JsonElement element, string path, List<Finding> findings, string sourceFile)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    Walk(prop.Value, string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}", findings, sourceFile);
                }
                break;

            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{path}[{i++}]", findings, sourceFile);
                }
                break;

            case JsonValueKind.String:
                InspectLeaf(path, element.GetString() ?? string.Empty, findings, sourceFile);
                break;
        }
    }

    private void InspectLeaf(string path, string value, List<Finding> findings, string sourceFile)
    {
        if (string.IsNullOrEmpty(value)) return;

        var keySegment = path.Split('.').LastOrDefault() ?? string.Empty;
        // Underscore-prefixed keys (e.g. "_comment_secrets") are a JSON-comment convention — skip.
        if (keySegment.StartsWith('_')) return;

        keySegment = keySegment.ToLowerInvariant();
        bool looksSensitive = SensitiveKeyMarkers.Any(m => keySegment.Contains(m, StringComparison.Ordinal));

        // Also flag connection strings that embed Password=/Pwd=
        bool connectionStringWithPassword = path.StartsWith("ConnectionStrings.", StringComparison.OrdinalIgnoreCase)
            && (value.Contains("Password=", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Pwd=", StringComparison.OrdinalIgnoreCase));

        if (!looksSensitive && !connectionStringWithPassword) return;

        if (AllowedValuePrefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal)))
        {
            return;
        }

        findings.Add(new Finding(
            RuleId, Title, Category,
            Severity.Warning,
            $"'{path}' appears to contain a plaintext secret in appsettings.json.",
            Location: sourceFile,
            Remediation: "Replace with an empty string and set the value via user secrets (dev) or Azure Key Vault reference (prod): \"@Microsoft.KeyVault(VaultName=...;SecretName=...)\"."));
    }
}
