namespace BoostRoot.Core.Profiles;

public enum TargetStatus { Applied, NotFound, Failed, Skipped }

public sealed record TargetResult(string ConfigId, string? Path, TargetStatus Status, int ChangedKeys, string? Error);

public sealed record ApplyReport(
    string OperationId,
    string ProfileId,
    BoostLevel Level,
    IReadOnlyList<TargetResult> Targets,
    bool Success,
    bool RolledBack);
