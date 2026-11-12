using BoostRoot.Core.Platform;
using BoostRoot.Core.Safety;

namespace BoostRoot.Core.Tools;

public sealed record StartupItem(string Name, string Command, RegistryHive Hive, string RunKey, bool Enabled);

/// <summary>
/// Lists Run-key startup apps and toggles them the same way Task Manager does (StartupApproved\Run byte 0x02 = on, 0x03 = off),
/// so changes are visible in Windows and fully reversible via the journal.
/// </summary>
public sealed class StartupManager(IRegistry registry, RegistryChanger changer)
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";


    public IReadOnlyList<StartupItem> List(Func<RegistryHive, IReadOnlyList<string>> runValueNames)
    {
        var items = new List<StartupItem>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var name in runValueNames(hive))
            {
                var cmd = registry.GetValue(hive, Run, name)?.Data ?? "";
                var state = registry.GetValue(hive, Approved, name);
                var enabled = state is null || state.Kind != RegistryValueKind.Binary || Convert.FromBase64String(state.Data) is not [3, ..];
                items.Add(new(name, cmd, hive, Run, enabled));
            }
        return items;
    }

    public bool SetEnabled(string operationId, StartupItem item, bool enabled)
    {
        var bytes = new byte[12];
        bytes[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        return changer.Set(operationId, item.Hive, Approved, item.Name, new RegistryValue(RegistryValueKind.Binary, Convert.ToBase64String(bytes)), "startup:" + item.Name);
    }
}
