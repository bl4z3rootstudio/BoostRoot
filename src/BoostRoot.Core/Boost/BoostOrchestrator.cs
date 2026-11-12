using BoostRoot.Core.Platform;
using BoostRoot.Core.Profiles;
using BoostRoot.Core.Safety;
using BoostRoot.Core.Tweaks;

namespace BoostRoot.Core.Boost;

public sealed record BoostRequest(
    GameProfile Profile,
    BoostLevel Level,
    IReadOnlyList<string> CloseProcessNames,
    IReadOnlyList<string> TweakIds,
    string? GameExePath,
    IReadOnlyList<string> GameProcessNames);

public enum StepStatus { Done, Skipped, Failed }

public sealed record StepResult(string StepId, StepStatus Status, string? Detail = null, int Changes = 0);

public sealed record BoostResult(string OperationId, bool Success, string? Error, IReadOnlyList<StepResult> Steps, ApplyReport? GameSettings);

public sealed class BoostContext(BoostRequest request, string operationId)
{
    public BoostRequest Request { get; } = request;
    public string OperationId { get; } = operationId;
    public ApplyReport? GameSettings { get; set; }
}

/// <summary>Optional extra step (e.g. GPU driver profile from Plan 04).</summary>
public interface IBoostStep
{
    string Id { get; }
    StepResult Run(BoostContext context);
}

/// <summary>Runs a boost: game settings → close apps → Windows tweaks → GPU. A failed game-settings step rolls everything back.</summary>
public sealed class BoostOrchestrator(
    ProfileApplier applier,
    IProcessManager processes,
    IRegistry registry,
    RegistryChanger registryChanger,
    PowerPlanChanger powerChanger,
    UndoService undo,
    IEnumerable<IBoostStep> extraSteps,
    Func<string>? newOperationId = null)
{
    public const string GameRunningError = "GameRunning";

    /// <summary>Processes that are never closed, whatever the user's list says.</summary>
    public static IReadOnlyList<string> NeverClose { get; } =
    [
        "audiodg", "csrss", "dwm", "explorer", "lsass", "services", "smss", "svchost", "wininit", "winlogon", "system", "fontdrvhost",
        "vgc", "vgtray", "easyanticheat", "easyanticheat_eos", "beservice", "battleye", "faceit", "faceitservice",
        "splitwire", "goodbyedpi", "zapret", "winws", "byedpi", "boostroot", "boostroot.agent",
        "nvcontainer", "nvdisplay.container", "radeonsoftware", "amdrsserv", "atieclxx",
    ];

    private static string Bare(string name) => Path.GetFileNameWithoutExtension(name).ToLowerInvariant();

    public static IReadOnlyList<string> FilterCloseList(IEnumerable<string> names) =>
        [.. names.Select(Bare).Where(n => n.Length > 0 && !NeverClose.Contains(n)).Distinct()];

    public BoostResult Run(BoostRequest request)
    {
        var opId = newOperationId?.Invoke() ?? $"boost-{request.Profile.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var ctx = new BoostContext(request, opId);
        var steps = new List<StepResult>();

        var running = processes.List();
        var gameNames = request.GameProcessNames.Select(Bare).ToHashSet();
        if (running.Any(p => gameNames.Contains(Bare(p.Name))))
            return new BoostResult(opId, false, GameRunningError, steps, null);

        // 1) Game settings (critical)
        try
        {
            var report = applier.Apply(request.Profile, request.Level, opId);
            ctx.GameSettings = report;
            var changes = report.Targets.Sum(t => t.ChangedKeys);
            steps.Add(new StepResult("game-settings", report.Success ? StepStatus.Done : StepStatus.Failed,
                report.Success ? null : report.Targets.LastOrDefault(t => t.Status == TargetStatus.Failed)?.Error, changes));
            if (!report.Success) return Fail(opId, "GameSettingsFailed", steps, report);
        }
        catch (ProtectedSettingException ex)
        {
            steps.Add(new StepResult("game-settings", StepStatus.Failed, ex.Message));
            return Fail(opId, "ProfileError", steps, null);
        }

        // 2) Close background apps (best effort)
        var toClose = FilterCloseList(request.CloseProcessNames).ToHashSet();
        var closed = 0;
        foreach (var p in running.Where(p => toClose.Contains(Bare(p.Name))))
            if (processes.Close(p.Id, TimeSpan.FromSeconds(4))) closed++;
        steps.Add(new StepResult("close-apps", toClose.Count == 0 ? StepStatus.Skipped : StepStatus.Done, null, closed));

        // 3) Windows tweaks (best effort; each journaled)
        var tweakChanges = 0;
        string? tweakError = null;
        foreach (var id in request.TweakIds)
        {
            try { tweakChanges += WindowsTweaks.Apply(WindowsTweaks.Get(id), opId, registry, registryChanger, powerChanger); }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException or System.Security.SecurityException) { tweakError = ex.Message; }
        }
        if (request.GameExePath is { Length: > 0 } exe)
        {
            try { if (WindowsTweaks.SetGpuPreference(exe, opId, registry, registryChanger)) tweakChanges++; }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { tweakError = ex.Message; }
        }
        steps.Add(new StepResult("windows", request.TweakIds.Count == 0 && request.GameExePath is null ? StepStatus.Skipped
            : tweakError is null ? StepStatus.Done : StepStatus.Failed, tweakError, tweakChanges));

        // 4) Extra steps (GPU driver profile etc.)
        foreach (var s in extraSteps)
        {
            try { steps.Add(s.Run(ctx)); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { steps.Add(new StepResult(s.Id, StepStatus.Failed, ex.Message)); }
        }

        return new BoostResult(opId, true, null, steps, ctx.GameSettings);
    }

    private BoostResult Fail(string opId, string error, List<StepResult> steps, ApplyReport? report)
    {
        try { undo.RevertOperation(opId); } catch (AggregateException) { error += "+RevertIncomplete"; }
        return new BoostResult(opId, false, error, steps, report);
    }
}
