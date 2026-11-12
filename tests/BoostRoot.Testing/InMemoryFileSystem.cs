using System.Text;
using System.Text.RegularExpressions;
using BoostRoot.Core.IO;

namespace BoostRoot.Testing;

/// <summary>Case-insensitive, '/'-normalised in-memory file system (Windows-like behaviour) for tests.</summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private sealed class Entry { public byte[] Data = []; public bool ReadOnly; public DateTime LastWrite = DateTime.UtcNow; }
    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public static string N(string p) => p.Replace('\\', '/').TrimEnd('/');

    public InMemoryFileSystem Put(string path, string text, DateTime? lastWrite = null) => PutBytes(path, Encoding.UTF8.GetBytes(text), lastWrite);

    public InMemoryFileSystem PutBytes(string path, byte[] data, DateTime? lastWrite = null)
    {
        path = N(path);
        AddDirs(Dir(path));
        _files[path] = new Entry { Data = data, LastWrite = lastWrite ?? DateTime.UtcNow };
        return this;
    }

    public string Text(string path) => Encoding.UTF8.GetString(ReadAllBytes(path));
    public void SetReadOnly(string path, bool value) => _files[N(path)].ReadOnly = value;
    public void SetLastWrite(string path, DateTime t) => _files[N(path)].LastWrite = t;
    public IReadOnlyCollection<string> AllFiles => _files.Keys;

    private static string Dir(string path) { var i = path.LastIndexOf('/'); return i <= 0 ? "/" : path[..i]; }

    private void AddDirs(string dir)
    {
        while (dir.Length > 0 && _dirs.Add(dir))
        {
            var i = dir.LastIndexOf('/');
            if (i <= 0) break;
            dir = dir[..i];
        }
    }

    public bool FileExists(string path) => _files.ContainsKey(N(path));
    public bool DirectoryExists(string path) => _dirs.Contains(N(path)) || N(path) is "" or "/";
    public byte[] ReadAllBytes(string path) => _files.TryGetValue(N(path), out var e) ? [.. e.Data] : throw new FileNotFoundException(path);

    public void WriteAllBytesAtomic(string path, byte[] data)
    {
        path = N(path);
        if (_files.TryGetValue(path, out var e) && e.ReadOnly) throw new UnauthorizedAccessException($"File is read-only: {path}");
        PutBytes(path, [.. data], DateTime.UtcNow);
    }

    public bool IsReadOnly(string path) => _files.TryGetValue(N(path), out var e) && e.ReadOnly;
    public void Delete(string path) => _files.Remove(N(path));
    public void CreateDirectory(string path) => AddDirs(N(path));
    public DateTime GetLastWriteTimeUtc(string path) => _files[N(path)].LastWrite;

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        var dir = N(directory);
        var rx = new Regex("^" + Regex.Escape(searchPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
        return _files.Keys.Where(f =>
        {
            if (!f.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase) && dir != "/") return false;
            var rest = dir == "/" ? f.TrimStart('/') : f[(dir.Length + 1)..];
            if (!recursive && rest.Contains('/')) return false;
            return rx.IsMatch(rest[(rest.LastIndexOf('/') + 1)..]);
        }).ToList();
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        var dir = N(directory);
        return _dirs.Where(d => Dir(d).Equals(dir == "" ? "/" : dir, StringComparison.OrdinalIgnoreCase) && !d.Equals(dir, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
