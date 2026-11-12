using BoostRoot.Core.Platform;

namespace BoostRoot.Testing;

public sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<string, RegistryValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private static string K(RegistryHive h, string key, string name) => $"{h}\\{key}\\\\{name}";
    public RegistryValue? GetValue(RegistryHive hive, string key, string name) => _values.GetValueOrDefault(K(hive, key, name));
    public void SetValue(RegistryHive hive, string key, string name, RegistryValue value) => _values[K(hive, key, name)] = value;
    public void DeleteValue(RegistryHive hive, string key, string name) => _values.Remove(K(hive, key, name));
    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string key) => [];
    public int Count => _values.Count;
}

public sealed class FakeProcessManager : IProcessManager
{
    public List<ProcessInfo> Running { get; } = [];
    public List<string> Closed { get; } = [];
    public List<(string File, string? Args)> Started { get; } = [];
    public IReadOnlyList<ProcessInfo> List() => [.. Running];
    public bool Close(int processId, TimeSpan gracefulTimeout)
    {
        var p = Running.Find(x => x.Id == processId);
        if (p is null) return false;
        Running.Remove(p);
        Closed.Add(p.Name);
        return true;
    }
    public bool Suspend(int processId) => true;
    public bool Resume(int processId) => true;
    public bool SetHighPriority(int processId) => true;
    public void Start(string fileName, string? arguments, bool useShellExecute) => Started.Add((fileName, arguments));
}

public sealed class FakePowerPlans(Guid? active = null) : IPowerPlans
{
    public Guid? Active { get; private set; } = active;
    public Guid? GetActive() => Active;
    public bool SetActive(Guid scheme) { Active = scheme; return true; }
}
