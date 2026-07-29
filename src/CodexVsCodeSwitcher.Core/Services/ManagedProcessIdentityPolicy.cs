using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ManagedProcessIdentityPolicy
{
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HandoffStartTolerance = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandoffWindow = TimeSpan.FromMinutes(1);

    public bool IsManagedRoot(
        ManagedVsCodeInstanceState expected,
        ProcessIdentityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.ProcessId == expected.RootProcessId
            && Math.Abs((evidence.StartTimeUtc - expected.RootProcessStartTimeUtc).TotalSeconds)
                <= StartTimeTolerance.TotalSeconds
            && SamePath(evidence.ExecutablePath, expected.ExecutablePath)
            && HasArgumentValue(
                evidence.CommandLineArguments,
                "--user-data-dir",
                expected.UserDataDirectory)
            && HasArgumentValue(
                evidence.CommandLineArguments,
                "--extensions-dir",
                expected.ExtensionsDirectory);
    }

    public bool IsManagedRootHandoffCandidate(
        ManagedVsCodeInstanceState expected,
        ProcessIdentityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(evidence);
        DateTimeOffset earliest = expected.LaunchTimestampUtc - HandoffStartTolerance;
        DateTimeOffset latest = expected.LaunchTimestampUtc + HandoffWindow;
        return evidence.ProcessId > 0
            && evidence.ProcessId != expected.RootProcessId
            && evidence.StartTimeUtc >= earliest
            && evidence.StartTimeUtc <= latest
            && SamePath(evidence.ExecutablePath, expected.ExecutablePath)
            && HasArgumentValue(
                evidence.CommandLineArguments,
                "--user-data-dir",
                expected.UserDataDirectory)
            && HasArgumentValue(
                evidence.CommandLineArguments,
                "--extensions-dir",
                expected.ExtensionsDirectory);
    }

    private static bool HasArgumentValue(
        IReadOnlyList<string> arguments,
        string option,
        string expectedValue)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index].Equals(option, StringComparison.OrdinalIgnoreCase)
                && SamePath(arguments[index + 1], expectedValue))
            {
                return true;
            }
        }

        string prefix = option + "=";
        return arguments.Any(argument =>
            argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && SamePath(argument[prefix.Length..], expectedValue));
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).Equals(
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
