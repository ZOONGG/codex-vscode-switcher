using System.Text.Json;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed record VsCodeProxySettingsPlan(
    IReadOnlyDictionary<string, JsonElement> Settings)
{
    public IReadOnlyList<string> SettingNames => Settings.Keys.Order(StringComparer.Ordinal).ToArray();
}

public sealed class VsCodeProxySettingsService
{
    private static readonly HashSet<string> ProxySupportValues =
        new(StringComparer.OrdinalIgnoreCase) { "off", "on", "fallback", "override" };
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly IProtectedPathPolicy protectedPaths;

    public VsCodeProxySettingsService(IProtectedPathPolicy protectedPaths)
        => this.protectedPaths = protectedPaths;

    public VsCodeProxySettingsPlan BuildPlan(string ordinarySettingsFile)
    {
        string source = Path.GetFullPath(ordinarySettingsFile);
        protectedPaths.AssertCanRead(source);
        if (!File.Exists(source))
        {
            return new VsCodeProxySettingsPlan(
                new Dictionary<string, JsonElement>(StringComparer.Ordinal));
        }

        Dictionary<string, JsonElement> sourceSettings = ReadSettings(source);
        var allowed = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (sourceSettings.TryGetValue("http.proxy", out JsonElement proxy)
            && IsSafeProxy(proxy))
        {
            allowed["http.proxy"] = proxy.Clone();
        }

        if (sourceSettings.TryGetValue("http.proxyStrictSSL", out JsonElement strictSsl)
            && strictSsl.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            allowed["http.proxyStrictSSL"] = strictSsl.Clone();
        }

        if (sourceSettings.TryGetValue("http.proxySupport", out JsonElement support)
            && support.ValueKind == JsonValueKind.String
            && ProxySupportValues.Contains(support.GetString() ?? string.Empty))
        {
            allowed["http.proxySupport"] = support.Clone();
        }

        return new VsCodeProxySettingsPlan(allowed);
    }

    public void Apply(string dedicatedUserDataDirectory, VsCodeProxySettingsPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string root = Path.GetFullPath(dedicatedUserDataDirectory);
        protectedPaths.AssertCanWrite(root);
        string settingsFile = Path.Combine(root, "User", "settings.json");
        protectedPaths.AssertCanWrite(settingsFile);
        Dictionary<string, JsonElement> destination = File.Exists(settingsFile)
            ? ReadSettings(settingsFile)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string name, JsonElement value) in plan.Settings)
        {
            if (name is "http.proxy" or "http.proxyStrictSSL" or "http.proxySupport")
            {
                destination[name] = value.Clone();
            }
        }

        AtomicJsonFile.Write(settingsFile, destination, SerializerOptions, protectedPaths);
    }

    private static bool IsSafeProxy(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string raw = value.GetString()?.Trim() ?? string.Empty;
        return Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static Dictionary<string, JsonElement> ReadSettings(string settingsFile)
    {
        try
        {
            using FileStream stream = File.OpenRead(settingsFile);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                stream,
                new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                }) ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("VS Code settings are not valid JSON.", exception);
        }
    }
}
