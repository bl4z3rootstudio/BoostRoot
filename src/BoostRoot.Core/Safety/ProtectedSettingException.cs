namespace BoostRoot.Core.Safety;

/// <summary>Thrown when code attempts to write a setting the user must keep (resolution, keybinds, sensitivity...).</summary>
public sealed class ProtectedSettingException(string configId, string key)
    : InvalidOperationException($"Setting '{configId}:{key}' is protected and must never be changed by BoostRoot.")
{
    public string ConfigId { get; } = configId;
    public string Key { get; } = key;
}
