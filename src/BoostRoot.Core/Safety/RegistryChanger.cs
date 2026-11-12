using System.Text.Json;
using BoostRoot.Core.Platform;

namespace BoostRoot.Core.Safety;

internal sealed record RegistryBackup(RegistryHive Hive, string Key, string Name, RegistryValue? Previous);

/// <summary>Journaled registry writes. The previous value (or its absence) is stored in the journal entry.</summary>
public sealed class RegistryChanger(IRegistry registry, Journal journal) : IUndoHandler
{
    public string Kind => JournalKinds.Registry;

    /// <summary>Returns false (and journals nothing) when the value is already as requested.</summary>
    public bool Set(string operationId, RegistryHive hive, string key, string name, RegistryValue value, string? description = null)
    {
        var previous = registry.GetValue(hive, key, name);
        if (previous == value) return false;
        var backup = JsonSerializer.Serialize(new RegistryBackup(hive, key, name, previous), SafetyJsonContext.Default.RegistryBackup);
        registry.SetValue(hive, key, name, value);
        journal.Record(operationId, Kind, $"{hive}\\{key}\\{name}", backup, previous is not null, description);
        return true;
    }

    public void Revert(JournalEntry entry)
    {
        var b = JsonSerializer.Deserialize(entry.BackupRef ?? "", SafetyJsonContext.Default.RegistryBackup)
            ?? throw new InvalidOperationException("Registry backup is empty.");
        if (b.Previous is null) registry.DeleteValue(b.Hive, b.Key, b.Name);
        else registry.SetValue(b.Hive, b.Key, b.Name, b.Previous);
    }
}

/// <summary>Journaled active power plan changes.</summary>
public sealed class PowerPlanChanger(IPowerPlans plans, Journal journal) : IUndoHandler
{
    public string Kind => JournalKinds.PowerPlan;

    public bool SetActive(string operationId, Guid scheme)
    {
        var previous = plans.GetActive();
        if (previous == scheme) return false;
        if (!plans.SetActive(scheme)) throw new InvalidOperationException($"Could not activate power plan {scheme}.");
        journal.Record(operationId, Kind, scheme.ToString(), previous?.ToString(), previous is not null, "power plan");
        return true;
    }

    public void Revert(JournalEntry entry)
    {
        if (Guid.TryParse(entry.BackupRef, out var prev) && !plans.SetActive(prev))
            throw new InvalidOperationException($"Could not restore power plan {prev}.");
    }
}
