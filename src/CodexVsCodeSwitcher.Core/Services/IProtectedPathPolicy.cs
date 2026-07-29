namespace CodexVsCodeSwitcher.Core.Services;

public interface IProtectedPathPolicy
{
    IReadOnlyList<string> ProtectedRoots { get; }
    bool IsProtected(string path);
    void AssertCanRead(string path);
    void AssertCanWrite(string path);
    void AssertCanCopy(string sourcePath, string destinationPath);
}
