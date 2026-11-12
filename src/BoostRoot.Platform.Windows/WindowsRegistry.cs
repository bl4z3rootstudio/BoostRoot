using System.Globalization;
using BoostRoot.Core.Platform;
using Win = Microsoft.Win32;

namespace BoostRoot.Platform.Windows;

public sealed class WindowsRegistry : IRegistry
{
    private static Win.RegistryKey Root(RegistryHive hive) =>
        Win.RegistryKey.OpenBaseKey(hive == RegistryHive.CurrentUser ? Win.RegistryHive.CurrentUser : Win.RegistryHive.LocalMachine, Win.RegistryView.Registry64);

    public RegistryValue? GetValue(RegistryHive hive, string key, string name)
    {
        using var root = Root(hive);
        using var k = root.OpenSubKey(key, false);
        if (k is null) return null;
        var v = k.GetValue(name, null, Win.RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (v is null) return null;
        return k.GetValueKind(name) switch
        {
            Win.RegistryValueKind.DWord => new RegistryValue(RegistryValueKind.DWord, ((int)v).ToString(CultureInfo.InvariantCulture)),
            Win.RegistryValueKind.QWord => new RegistryValue(RegistryValueKind.QWord, ((long)v).ToString(CultureInfo.InvariantCulture)),
            Win.RegistryValueKind.Binary => new RegistryValue(RegistryValueKind.Binary, Convert.ToBase64String((byte[])v)),
            Win.RegistryValueKind.ExpandString => new RegistryValue(RegistryValueKind.ExpandString, (string)v),
            _ => new RegistryValue(RegistryValueKind.String, v.ToString() ?? ""),
        };
    }

    public void SetValue(RegistryHive hive, string key, string name, RegistryValue value)
    {
        using var root = Root(hive);
        using var k = root.CreateSubKey(key, true) ?? throw new UnauthorizedAccessException($"Cannot open {hive}\\{key}");
        switch (value.Kind)
        {
            case RegistryValueKind.DWord: k.SetValue(name, int.Parse(value.Data, CultureInfo.InvariantCulture), Win.RegistryValueKind.DWord); break;
            case RegistryValueKind.QWord: k.SetValue(name, long.Parse(value.Data, CultureInfo.InvariantCulture), Win.RegistryValueKind.QWord); break;
            case RegistryValueKind.Binary: k.SetValue(name, Convert.FromBase64String(value.Data), Win.RegistryValueKind.Binary); break;
            case RegistryValueKind.ExpandString: k.SetValue(name, value.Data, Win.RegistryValueKind.ExpandString); break;
            default: k.SetValue(name, value.Data, Win.RegistryValueKind.String); break;
        }
    }

    public void DeleteValue(RegistryHive hive, string key, string name)
    {
        using var root = Root(hive);
        using var k = root.OpenSubKey(key, true);
        k?.DeleteValue(name, false);
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string key)
    {
        using var root = Root(hive);
        using var k = root.OpenSubKey(key, false);
        return k?.GetSubKeyNames() ?? [];
    }
}
