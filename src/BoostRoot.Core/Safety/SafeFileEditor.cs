using BoostRoot.Core.IO;

namespace BoostRoot.Core.Safety;

public sealed record EditResult(bool Success, string Path, string? Error);

/// <summary>Backup → atomic write → read-back verify → journal. Any failure restores the original bytes.</summary>
public sealed class SafeFileEditor(IFileSystem fs, Journal journal, string backupDirectory)
{
    public const string NotFound = "NotFound";

    public EditResult Apply(string operationId, string path, Func<string, string> transformNormalizedText, string description)
    {
        if (!fs.FileExists(path)) return new EditResult(false, path, NotFound);

        byte[] original;
        try { original = fs.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new EditResult(false, path, ex.Message); }

        var file = TextFile.Decode(original);
        var transformed = file.WithText(transformNormalizedText(file.Text)); // ProtectedSettingException propagates by design
        var updated = transformed.Encode();
        if (updated.AsSpan().SequenceEqual(original)) return new EditResult(true, path, null);

        var backupName = Guid.NewGuid().ToString("N") + ".bak";
        var backupPath = Path.Combine(backupDirectory, backupName);
        try
        {
            fs.CreateDirectory(backupDirectory);
            fs.WriteAllBytesAtomic(backupPath, original);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new EditResult(false, path, "Backup failed: " + ex.Message);
        }

        try
        {
            fs.WriteAllBytesAtomic(path, updated);
            if (!fs.ReadAllBytes(path).AsSpan().SequenceEqual(updated))
                throw new IOException("Verification after write failed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryRestore(path, original);
            return new EditResult(false, path, ex.Message);
        }

        journal.Record(operationId, JournalKinds.File, path, backupPath, true, description);
        return new EditResult(true, path, null);
    }

    /// <summary>Restores every file changed by the operation, newest first. Failures are collected and thrown at the end.</summary>
    public void RevertOperation(string operationId)
    {
        var errors = new List<Exception>();
        foreach (var e in journal.ForOperation(operationId).Where(e => e.Kind == JournalKinds.File && !e.Reverted).Reverse())
        {
            try
            {
                if (e.BackupRef is null || !fs.FileExists(e.BackupRef)) throw new FileNotFoundException("Backup missing.", e.BackupRef);
                fs.WriteAllBytesAtomic(e.Target, fs.ReadAllBytes(e.BackupRef));
                journal.MarkReverted(e.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(new IOException($"Could not restore '{e.Target}': {ex.Message}", ex));
            }
        }
        if (errors.Count > 0) throw new AggregateException("Some changes could not be reverted.", errors);
    }

    private void TryRestore(string path, byte[] original)
    {
        try
        {
            if (!fs.ReadAllBytes(path).AsSpan().SequenceEqual(original)) fs.WriteAllBytesAtomic(path, original);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
