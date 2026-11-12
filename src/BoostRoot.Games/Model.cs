using BoostRoot.Core.IO;
using BoostRoot.Core.Platform;
using BoostRoot.Core.Profiles;

namespace BoostRoot.Games;

/// <param name="Source">steam | epic | riot | xbox | minecraft | roblox | manual</param>
/// <param name="Extra">Source-specific data (e.g. epic catalog ids, minecraft instance dir).</param>
public sealed record InstalledGame(string Source, string SourceId, string Name, string InstallPath, IReadOnlyDictionary<string, string> Extra);

public interface IGameLocator
{
    IReadOnlyList<InstalledGame> Locate();
}

/// <summary>Environment access the locators need (kept tiny so tests can fake it).</summary>
public sealed record LocatorEnvironment(
    IFileSystem Fs,
    IRegistry Registry,
    string ProgramData,
    string AppData,
    string LocalAppData,
    IReadOnlyList<string> DriveRoots);

public sealed record CatalogEntry(GameProfile? Profile, InstalledGame? Game)
{
    public bool IsInstalled => Game is not null;
    public string DisplayName => Profile?.Name ?? Game?.Name ?? "";
}

public sealed record LaunchCommand(string FileName, string? Arguments, bool UseShellExecute);
