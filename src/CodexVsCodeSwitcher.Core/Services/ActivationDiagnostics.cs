using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed record ActivationDiagnostic(
    ActivationFailureCategory ErrorCategory,
    DateTimeOffset TimestampUtc,
    string AppVersion,
    string? VsCodeExecutablePath,
    string DedicatedUserDataDirectory,
    string DedicatedExtensionsDirectory,
    string ProfileId,
    string? WorkspacePath,
    IReadOnlyList<ManagedProcessDiagnostic> Processes,
    string? TimeoutStage,
    string? ExceptionType,
    string? SanitizedMessage);

public static partial class DiagnosticTextSanitizer
{
    private const int MaximumLength = 500;

    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string sanitized = EmailPattern().Replace(value, "<redacted-email>");
        sanitized = SecretAssignmentPattern().Replace(
            sanitized,
            match => $"{match.Groups[1].Value}=<redacted>");
        sanitized = ControlCharacterPattern().Replace(sanitized, " ");
        sanitized = sanitized.Trim();
        return sanitized.Length <= MaximumLength
            ? sanitized
            : sanitized[..MaximumLength];
    }

    [GeneratedRegex(
        @"(?i)\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(
        @"(?i)\b(access[_-]?token|refresh[_-]?token|id[_-]?token|session[_-]?id|authorization|api[_-]?key|password|secret)\s*[:=]\s*[^\s,;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")]
    private static partial Regex ControlCharacterPattern();
}

public static class ActivationDiagnosticsFormatter
{
    public static string Format(ActivationDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var output = new StringBuilder();
        Add(output, "errorCategory", diagnostic.ErrorCategory.ToString());
        Add(output, "timestampUtc", diagnostic.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Add(output, "appVersion", diagnostic.AppVersion);
        Add(output, "vsCodeExecutablePath", diagnostic.VsCodeExecutablePath);
        Add(output, "dedicatedUserDataDirectory", diagnostic.DedicatedUserDataDirectory);
        Add(output, "dedicatedExtensionsDirectory", diagnostic.DedicatedExtensionsDirectory);
        Add(output, "profileId", diagnostic.ProfileId);
        Add(output, "workspacePath", diagnostic.WorkspacePath);
        foreach (ManagedProcessDiagnostic process in diagnostic.Processes)
        {
            Add(
                output,
                "process",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{process.ProcessId}@{process.StartTimeUtc.ToUniversalTime():O}"));
        }

        Add(output, "timeoutStage", diagnostic.TimeoutStage);
        Add(output, "exceptionType", diagnostic.ExceptionType);
        Add(output, "message", DiagnosticTextSanitizer.Sanitize(diagnostic.SanitizedMessage));
        return output.ToString().TrimEnd();
    }

    private static void Add(StringBuilder output, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _ = output.Append(name).Append(": ").Append(value).Append('\n');
        }
    }
}
