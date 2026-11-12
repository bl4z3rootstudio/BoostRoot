using System.Globalization;
using System.Text;
using System.Text.Json;
using BoostRoot.Core.IO;

namespace BoostRoot.Core.Safety;

/// <summary>Append-only record of every change BoostRoot makes, so any operation can be undone later.</summary>
public sealed class Journal(IFileSystem fs, string directory, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private const string RevertedFile = "reverted.jsonl";

    public string Directory { get; } = directory;

    public JournalEntry Record(string operationId, string kind, string target, string? backupRef, bool targetExisted, string? description)
    {
        var now = _time.GetUtcNow();
        var entry = new JournalEntry(
            now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8],
            now, operationId, kind, target, backupRef, targetExisted, description, false);
        Append(now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl",
            JsonSerializer.Serialize(entry, SafetyJsonContext.Default.JournalEntry));
        return entry;
    }

    public void MarkReverted(string entryId) =>
        Append(RevertedFile, JsonSerializer.Serialize(new RevertMark(entryId, _time.GetUtcNow()), SafetyJsonContext.Default.RevertMark));

    public IReadOnlyList<JournalEntry> ReadAll()
    {
        lock (_gate)
        {
            var reverted = new HashSet<string>(StringComparer.Ordinal);
            var revPath = Path.Combine(Directory, RevertedFile);
            foreach (var line in Lines(revPath))
                if (JsonSerializer.Deserialize(line, SafetyJsonContext.Default.RevertMark) is { } m) reverted.Add(m.EntryId);

            var entries = new List<JournalEntry>();
            foreach (var file in fs.EnumerateFiles(Directory, "*.jsonl", false).Where(f => !f.EndsWith(RevertedFile, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
                foreach (var line in Lines(file))
                    if (JsonSerializer.Deserialize(line, SafetyJsonContext.Default.JournalEntry) is { } e)
                        entries.Add(e with { Reverted = reverted.Contains(e.Id) });
            return entries;
        }
    }

    public IReadOnlyList<JournalEntry> ForOperation(string operationId) =>
        [.. ReadAll().Where(e => e.OperationId == operationId)];

    private IEnumerable<string> Lines(string path) =>
        fs.FileExists(path)
            ? TextFile.Decode(fs.ReadAllBytes(path)).Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : [];

    private void Append(string fileName, string line)
    {
        lock (_gate)
        {
            fs.CreateDirectory(Directory);
            var path = Path.Combine(Directory, fileName);
            var existing = fs.FileExists(path) ? fs.ReadAllBytes(path) : [];
            var add = Encoding.UTF8.GetBytes(line + "\n");
            var data = new byte[existing.Length + add.Length];
            existing.CopyTo(data, 0);
            add.CopyTo(data, existing.Length);
            fs.WriteAllBytesAtomic(path, data);
        }
    }
}
