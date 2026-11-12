using BoostRoot.Core.Config;
using BoostRoot.Core.Safety;

namespace BoostRoot.Core.Profiles;

/// <summary>Writes a profile level's settings into the game's config files. All-or-nothing per operation.</summary>
public sealed class ProfileApplier(PathResolver resolver, SafeFileEditor editor)
{
    public ApplyReport Apply(GameProfile profile, BoostLevel level, string operationId) =>
        Apply(profile, level, operationId, extraSets: null, guardOverride: null);

    /// <summary>Used by 4:3 stretched mode: extra keys are allowed only through the exempted guard.</summary>
    public ApplyReport Apply(GameProfile profile, BoostLevel level, string operationId,
        IReadOnlyDictionary<string, string>? extraSets, NeverTouchGuard? guardOverride)
    {
        var guard = guardOverride ?? new NeverTouchGuard(profile.NeverTouch);
        var sets = new Dictionary<string, string>(profile.Levels[level].Set, StringComparer.Ordinal);
        if (extraSets is not null)
            foreach (var kv in extraSets) sets[kv.Key] = kv.Value;

        var byConfig = new Dictionary<string, List<(string Key, string Value)>>(StringComparer.Ordinal);
        foreach (var (configKey, value) in sets)
        {
            var (cid, key) = GameProfile.SplitSetKey(configKey);
            guard.EnsureAllowed(cid, key); // before any write
            if (!byConfig.TryGetValue(cid, out var list)) byConfig[cid] = list = [];
            list.Add((key, value));
        }

        var results = new List<TargetResult>();
        foreach (var target in profile.Configs)
        {
            if (!byConfig.TryGetValue(target.Id, out var keys)) continue;
            var paths = resolver.Resolve(target);
            if (paths.Count == 0)
            {
                results.Add(new TargetResult(target.Id, null, TargetStatus.NotFound, 0, null));
                continue;
            }

            var format = ConfigFormats.Get(target.Format, target.Separator);
            foreach (var path in paths)
            {
                var changed = 0;
                var r = editor.Apply(operationId, path, text =>
                {
                    changed = 0;
                    var doc = format.Parse(text);
                    foreach (var (key, value) in keys)
                    {
                        if (string.Equals(doc.Get(key), value, StringComparison.Ordinal)) continue;
                        doc.Set(key, value);
                        changed++;
                    }
                    return changed == 0 ? text : doc.Serialize();
                }, $"{profile.Id}/{level}/{target.Id}");

                if (r.Success)
                {
                    results.Add(new TargetResult(target.Id, path, TargetStatus.Applied, changed, null));
                }
                else if (r.Error == SafeFileEditor.NotFound)
                {
                    results.Add(new TargetResult(target.Id, path, TargetStatus.NotFound, 0, null));
                }
                else
                {
                    results.Add(new TargetResult(target.Id, path, TargetStatus.Failed, 0, r.Error));
                    editor.RevertOperation(operationId);
                    return new ApplyReport(operationId, profile.Id, level, results, false, true);
                }
            }
        }
        return new ApplyReport(operationId, profile.Id, level, results, true, false);
    }
}
