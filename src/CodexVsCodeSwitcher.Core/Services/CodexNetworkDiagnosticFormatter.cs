using System.Globalization;
using System.Text;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public static class CodexNetworkDiagnosticFormatter
{
    public static string Format(CodexNetworkDiagnosticReport report, LanguagePreference language)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder();
        Add(output, LocalizationCatalog.Text(language, "NetworkDiagnosticMode"),
            LocalizationCatalog.Text(language,
                report.Context.ExtensionMode == VsCodeExtensionMode.Shared
                    ? "SharedExtensionsMode"
                    : "IsolatedExtensionsMode"));
        Add(output, LocalizationCatalog.Text(language, "NetworkDiagnosticExtensionsPath"), report.Context.ExtensionsDirectory);
        Add(output, LocalizationCatalog.Text(language, "NetworkDiagnosticExtensionPath"), report.Context.ExtensionPath ?? "-");
        Add(output, LocalizationCatalog.Text(language, "NetworkDiagnosticBackendPath"), report.Context.BackendExecutablePath ?? "-");
        Add(output, LocalizationCatalog.Text(language, "NetworkDiagnosticSystemProxy"),
            LocalizationCatalog.Text(language,
                report.SystemProxyConfigured ? "SystemProxyDetected" : "SystemProxyDirect"));
        Add(output, LocalizationCatalog.Text(language, "NetworkDiagnosticEnvironmentNames"),
            report.PresentNetworkEnvironmentVariables.Count == 0
                ? "-"
                : string.Join(", ", report.PresentNetworkEnvironmentVariables));
        if (report.Context.ManagedProcesses is { Count: > 0 } processes)
        {
            output.AppendLine();
            output.AppendLine(LocalizationCatalog.Text(language, "NetworkDiagnosticManagedProcesses") + ":");
            foreach (BackendProcessPathDiagnostic process in processes)
            {
                output.Append("- ")
                    .Append(process.ProcessName)
                    .Append(" | ")
                    .Append(process.ExecutablePath)
                    .Append(" | parent PID ")
                    .Append(process.ParentProcessId.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ")
                    .Append(process.StartTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(process.ExtensionDirectory))
                {
                    output.Append(" | extension: ").Append(process.ExtensionDirectory);
                }

                output.AppendLine();
            }
        }

        output.AppendLine();
        foreach (NetworkDiagnosticStageResult stage in report.Stages)
        {
            string stageName = LocalizationCatalog.Text(language, "NetworkStage" + stage.Stage);
            string status = LocalizationCatalog.Text(language, "NetworkStatus" + stage.Status);
            output.Append(stageName)
                .Append(": ")
                .Append(status)
                .Append(" · ")
                .Append(stage.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture))
                .Append(" ms");
            if (stage.HttpStatusCode is int httpStatus)
            {
                output.Append(" · HTTP ").Append(httpStatus.ToString(CultureInfo.InvariantCulture));
            }

            if (stage.FailureCategory != NetworkFailureCategory.None)
            {
                output.Append(" · ")
                    .Append(LocalizationCatalog.Text(
                        language,
                        "NetworkFailure" + stage.FailureCategory));
            }

            if (!string.IsNullOrWhiteSpace(stage.SanitizedError))
            {
                output.AppendLine();
                output.Append("  ").Append(DiagnosticTextSanitizer.Sanitize(stage.SanitizedError));
            }

            output.AppendLine();
        }

        return output.ToString().TrimEnd();
    }

    private static void Add(StringBuilder output, string label, string value)
        => output.Append(label).Append(": ").AppendLine(value);
}
