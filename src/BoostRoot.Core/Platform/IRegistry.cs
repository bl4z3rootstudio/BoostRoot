namespace BoostRoot.Core.Platform;

public enum RegistryHive { CurrentUser, LocalMachine }

public enum RegistryValueKind { String, DWord, QWord, Binary, ExpandString }

public sealed record RegistryValue(RegistryValueKind Kind, string Data)
{
    public static RegistryValue Dword(int v) => new(RegistryValueKind.DWord, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static RegistryValue Str(string v) => new(RegistryValueKind.String, v);
}

/// <summary>Registry abstraction. Binary data is base64 in <see cref="RegistryValue.Data"/>; numbers are invariant decimal text.</summary>
public interface IRegistry
{
    RegistryValue? GetValue(RegistryHive hive, string key, string name);
    void SetValue(RegistryHive hive, string key, string name, RegistryValue value);
    void DeleteValue(RegistryHive hive, string key, string name);
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string key);
}

public sealed record ProcessInfo(int Id, string Name, string? Path);

public interface IProcessManager
{
    IReadOnlyList<ProcessInfo> List();
    /// <summary>Asks the process to close (main window), then kills it after the timeout. Returns true if it is gone.</summary>
    bool Close(int processId, TimeSpan gracefulTimeout);
    bool Suspend(int processId);
    bool Resume(int processId);
    bool SetHighPriority(int processId);
    void Start(string fileName, string? arguments, bool useShellExecute);
}

public interface IPowerPlans
{
    Guid? GetActive();
    bool SetActive(Guid scheme);
}

public static class PowerSchemes
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
}
