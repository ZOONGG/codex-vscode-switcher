using System.Reflection;
using System.Security.Cryptography;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class BundledCompanionExtensionProvisioner
{
    private const int MaximumAssetBytes = 1024 * 1024;
    private static readonly CompanionAsset[] Assets =
    [
        new("package.json", "CodexVsCodeSwitcher.Companion.package.json"),
        new("extension.js", "CodexVsCodeSwitcher.Companion.extension.js"),
        new("companion-core.js", "CodexVsCodeSwitcher.Companion.companion-core.js"),
    ];

    private readonly Assembly resourceAssembly;
    private readonly IProtectedPathPolicy protectedPaths;

    public BundledCompanionExtensionProvisioner(IProtectedPathPolicy protectedPaths)
        : this(typeof(BundledCompanionExtensionProvisioner).Assembly, protectedPaths)
    {
    }

    internal BundledCompanionExtensionProvisioner(
        Assembly resourceAssembly,
        IProtectedPathPolicy protectedPaths)
    {
        this.resourceAssembly = resourceAssembly;
        this.protectedPaths = protectedPaths;
    }

    public string Provision(string destinationDirectory)
    {
        string destination = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(destinationDirectory));
        protectedPaths.AssertCanWrite(destination);
        Directory.CreateDirectory(destination);

        foreach (CompanionAsset asset in Assets)
        {
            byte[] expected = ReadAsset(asset);
            string target = Path.Combine(destination, asset.FileName);
            protectedPaths.AssertCanWrite(target);
            if (HasExpectedContents(target, expected))
            {
                continue;
            }

            WriteAtomically(target, expected);
        }

        return destination;
    }

    private byte[] ReadAsset(CompanionAsset asset)
    {
        using Stream stream = resourceAssembly.GetManifestResourceStream(asset.ResourceName)
            ?? throw new InvalidOperationException(
                $"The bundled companion asset is unavailable: {asset.FileName}");
        if (stream.Length is <= 0 or > MaximumAssetBytes)
        {
            throw new InvalidDataException(
                $"The bundled companion asset has an invalid size: {asset.FileName}");
        }

        using var buffer = new MemoryStream(capacity: checked((int)stream.Length));
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool HasExpectedContents(string target, byte[] expected)
    {
        if (!File.Exists(target))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(target);
            if (info.Length != expected.Length || info.Length > MaximumAssetBytes)
            {
                return false;
            }

            using FileStream existing = new(
                target,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            byte[] actualHash = SHA256.HashData(existing);
            byte[] expectedHash = SHA256.HashData(expected);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteAtomically(string target, byte[] contents)
    {
        string directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("The companion target directory is unavailable.");
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(target)}-{Guid.NewGuid():N}.tmp");
        protectedPaths.AssertCanWrite(temporary);
        try
        {
            using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(target))
            {
                File.Replace(temporary, target, null);
            }
            else
            {
                File.Move(temporary, target);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private sealed record CompanionAsset(string FileName, string ResourceName);
}
