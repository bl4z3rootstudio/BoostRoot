namespace BoostRoot.Core.IO;

/// <summary>File system abstraction so all file logic is testable without touching a real disk.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    byte[] ReadAllBytes(string path);
    /// <summary>Writes via a temp file + replace. Throws <see cref="UnauthorizedAccessException"/> if the target is read-only (never clears the attribute).</summary>
    void WriteAllBytesAtomic(string path, byte[] data);
    bool IsReadOnly(string path);
    void Delete(string path);
    void CreateDirectory(string path);
    DateTime GetLastWriteTimeUtc(string path);
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive);
    IEnumerable<string> EnumerateDirectories(string directory);
}
