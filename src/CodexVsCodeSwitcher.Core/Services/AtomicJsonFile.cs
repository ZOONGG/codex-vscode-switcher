using System.Text.Json;

namespace CodexVsCodeSwitcher.Core.Services;

internal static class AtomicJsonFile
{
    public static void Write<T>(
        string targetPath,
        T value,
        JsonSerializerOptions options,
        IProtectedPathPolicy protectedPaths)
    {
        string fullPath = Path.GetFullPath(targetPath);
        protectedPaths.AssertCanWrite(fullPath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Target directory is unavailable.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}-{Guid.NewGuid():N}.tmp");
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
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                File.Replace(temporary, fullPath, null);
            }
            else
            {
                File.Move(temporary, fullPath);
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
}
