namespace BoostRoot.Core.Safety;

/// <summary>Reverts one journal entry of a given kind.</summary>
public interface IUndoHandler
{
    string Kind { get; }
    void Revert(JournalEntry entry);
}

/// <summary>Reverts any operation by dispatching each journal entry (newest first) to the handler for its kind.</summary>
public sealed class UndoService(Journal journal, IEnumerable<IUndoHandler> handlers)
{
    private readonly Dictionary<string, IUndoHandler> _handlers = handlers.ToDictionary(h => h.Kind, StringComparer.Ordinal);

    public void RevertOperation(string operationId) => Revert(journal.ForOperation(operationId));

    public void RevertAll() => Revert(journal.ReadAll());

    /// <summary>Operation ids that still have un-reverted entries, newest first.</summary>
    public IReadOnlyList<string> ActiveOperations() =>
        [.. journal.ReadAll().Where(e => !e.Reverted).OrderByDescending(e => e.At).Select(e => e.OperationId).Distinct()];

    private void Revert(IReadOnlyList<JournalEntry> entries)
    {
        var errors = new List<Exception>();
        foreach (var e in entries.Where(e => !e.Reverted).Reverse())
        {
            if (!_handlers.TryGetValue(e.Kind, out var h))
            {
                errors.Add(new InvalidOperationException($"No undo handler for '{e.Kind}' ({e.Target})."));
                continue;
            }
            try
            {
                h.Revert(e);
                journal.MarkReverted(e.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                errors.Add(ex);
            }
        }
        if (errors.Count > 0) throw new AggregateException("Some changes could not be reverted.", errors);
    }
}

/// <summary>Undo for <see cref="JournalKinds.File"/> entries (restores the backup bytes).</summary>
public sealed class FileUndoHandler(IO.IFileSystem fs) : IUndoHandler
{
    public string Kind => JournalKinds.File;

    public void Revert(JournalEntry entry)
    {
        if (entry.BackupRef is null || !fs.FileExists(entry.BackupRef)) throw new FileNotFoundException("Backup missing.", entry.BackupRef);
        fs.WriteAllBytesAtomic(entry.Target, fs.ReadAllBytes(entry.BackupRef));
    }
}
