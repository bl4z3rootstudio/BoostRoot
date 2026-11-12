namespace BoostRoot.Core.Safety;

public sealed record JournalEntry(
    string Id,
    DateTimeOffset At,
    string OperationId,
    string Kind,
    string Target,
    string? BackupRef,
    bool TargetExisted,
    string? Description,
    bool Reverted);

public static class JournalKinds
{
    public const string File = "file";
    public const string Registry = "registry";
    public const string Service = "service";
    public const string PowerPlan = "powerplan";
    public const string GpuProfile = "gpuprofile";
    public const string Startup = "startup";
    public const string Process = "process";
    public const string Display = "display";
}
