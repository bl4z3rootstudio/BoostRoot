using BoostRoot.Core.Profiles;
using BoostRoot.Core.Safety;
using BoostRoot.Core.Scoring;
using BoostRoot.Testing;

namespace BoostRoot.Core.Tests;

public class JournalAndEditorTests
{
    private readonly InMemoryFileSystem _fs = new();
    private Journal NewJournal() => new(_fs, "C:/BR/journal");
    private SafeFileEditor NewEditor(Journal j) => new(_fs, j, "C:/BR/backup");

    [Fact]
    public void Journal_Record_And_ReadAll_Survives_NewInstance()
    {
        NewJournal().Record("op1", JournalKinds.File, "C:/a.ini", "C:/b.bak", true, "d");
        var all = NewJournal().ReadAll();
        Assert.Single(all);
        Assert.Equal("op1", all[0].OperationId);
    }

    [Fact]
    public void Journal_MarkReverted_ReflectedInReadAll()
    {
        var j = NewJournal();
        var e = j.Record("op1", JournalKinds.File, "C:/a.ini", null, true, null);
        j.MarkReverted(e.Id);
        Assert.True(NewJournal().ReadAll().Single().Reverted);
    }

    [Fact]
    public void Editor_Apply_WritesBackupAndJournal()
    {
        _fs.Put("C:/g/a.ini", "x=1\n");
        var j = NewJournal();
        var r = NewEditor(j).Apply("op", "C:/g/a.ini", t => t.Replace("1", "2"), "test");
        Assert.True(r.Success);
        Assert.Equal("x=2\n", _fs.Text("C:/g/a.ini"));
        var entry = Assert.Single(j.ForOperation("op"));
        Assert.Equal("x=1\n", _fs.Text(entry.BackupRef!));
    }

    [Fact]
    public void Editor_Apply_MissingFile_ReturnsNotFound_NoJournal()
    {
        var j = NewJournal();
        var r = NewEditor(j).Apply("op", "C:/none.ini", t => t, "test");
        Assert.Equal(SafeFileEditor.NotFound, r.Error);
        Assert.Empty(j.ReadAll());
    }

    [Fact]
    public void Editor_Apply_ReadOnly_ReturnsFailure_FileUnchanged()
    {
        _fs.Put("C:/g/a.ini", "x=1\n");
        _fs.SetReadOnly("C:/g/a.ini", true);
        var r = NewEditor(NewJournal()).Apply("op", "C:/g/a.ini", t => "x=9\n", "test");
        Assert.False(r.Success);
        Assert.Equal("x=1\n", _fs.Text("C:/g/a.ini"));
    }

    [Fact]
    public void Editor_Revert_RestoresExactBytes_InReverseOrder()
    {
        _fs.PutBytes("C:/g/a.ini", [0xEF, 0xBB, 0xBF, (byte)'x', (byte)'=', (byte)'1', 13, 10]);
        var original = _fs.ReadAllBytes("C:/g/a.ini");
        var j = NewJournal();
        var ed = NewEditor(j);
        ed.Apply("op", "C:/g/a.ini", t => t.Replace("1", "2"), "1");
        ed.Apply("op", "C:/g/a.ini", t => t.Replace("2", "3"), "2");
        ed.RevertOperation("op");
        Assert.Equal(original, _fs.ReadAllBytes("C:/g/a.ini"));
        Assert.All(j.ForOperation("op"), e => Assert.True(e.Reverted));
    }

    [Fact]
    public void Editor_Transform_Throws_Protected_Propagates_AndFileUnchanged()
    {
        _fs.Put("C:/g/a.ini", "x=1\n");
        Assert.Throws<ProtectedSettingException>(() =>
            NewEditor(NewJournal()).Apply("op", "C:/g/a.ini", _ => throw new ProtectedSettingException("c", "k"), "t"));
        Assert.Equal("x=1\n", _fs.Text("C:/g/a.ini"));
    }
}

public class ProfileTests
{
    private static string ExampleJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "_example.json"));
    private static GameProfile Example => ProfileLoader.Parse(ExampleJson);

    private static GameProfile WithSet(GameProfile p, BoostLevel level, string key, string value)
    {
        var levels = p.Levels.ToDictionary();
        levels[level] = levels[level] with { Set = new Dictionary<string, string>(levels[level].Set) { [key] = value } };
        return p with { Levels = levels };
    }

    [Fact]
    public void Parse_Example_Profile()
    {
        var p = Example;
        Assert.Equal(4, p.Levels.Count);
        Assert.True(p.Levels[BoostLevel.Competitive].Gpu!.LowLatency);
        Assert.Equal("high", p.Notes["gus:ScalabilityGroups/sg.ShadowQuality"].FpsImpact);
    }

    [Fact] public void Validate_Example_NoErrors() => Assert.Empty(ProfileValidator.Validate(Example));

    [Fact] public void Validate_SetOnProtectedKey_Error() =>
        Assert.Contains(ProfileValidator.Validate(WithSet(Example, BoostLevel.Quality, "gus:Mouse/Sensitivity", "1")), e => e.Contains("protected"));

    [Fact] public void Validate_ProfileNeverTouch_Error() =>
        Assert.Contains(ProfileValidator.Validate(WithSet(Example, BoostLevel.Quality, "gus:Display/Gamma", "1")), e => e.Contains("protected"));

    [Fact] public void Validate_UnknownConfigId_Error() =>
        Assert.Contains(ProfileValidator.Validate(WithSet(Example, BoostLevel.Quality, "zzz:A/b", "1")), e => e.Contains("unknown config"));

    [Fact] public void Validate_FovKey_Error() =>
        Assert.Contains(ProfileValidator.Validate(WithSet(Example, BoostLevel.Quality, "gus:Camera/FieldOfViewFov", "90")), e => e.Contains("FOV"));

    [Fact]
    public void Validate_MissingLevel_Error()
    {
        var p = Example with { Levels = Example.Levels.Where(kv => kv.Key != BoostLevel.MaxFps).ToDictionary() };
        Assert.Contains(ProfileValidator.Validate(p), e => e.Contains("missing level"));
    }

    [Fact]
    public void Validate_CompetitiveWithUpscaler_Error()
    {
        var levels = Example.Levels.ToDictionary();
        levels[BoostLevel.Quality] = levels[BoostLevel.Quality] with { Gpu = new GpuPrefs(Upscaler: "quality") };
        Assert.Contains(ProfileValidator.Validate(Example with { Levels = levels }), e => e.Contains("upscaler"));
    }

    [Fact] public void Validate_UnknownFormat_Error() =>
        Assert.Contains(ProfileValidator.Validate(Example with { Configs = [Example.Configs[0] with { Format = "yaml" }] }), e => e.Contains("unknown format"));

    [Fact] public void Validate_NoDefaultRecommendRule_Error() =>
        Assert.Contains(ProfileValidator.Validate(Example with { Recommend = [new RecommendRule(40, BoostLevel.MaxFps)] }), e => e.Contains("default rule"));

    [Fact]
    public void LoadDirectory_OneBadFile_ThrowsWithFileName()
    {
        var fs = new InMemoryFileSystem().Put("C:/p/good.json", ExampleJson).Put("C:/p/bad.json", ExampleJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"));
        var ex = Assert.Throws<ProfileLoadException>(() => ProfileLoader.LoadDirectory(fs, "C:/p"));
        Assert.EndsWith("bad.json", ex.File);
    }

    [Fact]
    public void LoadDirectory_DuplicateId_Throws()
    {
        var fs = new InMemoryFileSystem().Put("C:/p/a.json", ExampleJson).Put("C:/p/b.json", ExampleJson);
        Assert.Throws<ProfileLoadException>(() => ProfileLoader.LoadDirectory(fs, "C:/p"));
    }
}

public class ApplierTests
{
    private const string Gus = "C:/Users/u/AppData/Local/VALORANT/Saved/Config";
    private const string Ini = "[/Script/ShooterGame.ShooterGameUserSettings]\r\nResolutionSizeX=1440\r\nResolutionSizeY=1080\r\n\r\n[ScalabilityGroups]\r\nsg.ShadowQuality=3\r\nsg.EffectsQuality=3\r\nsg.PostProcessQuality=3\r\n";
    private readonly InMemoryFileSystem _fs = new();
    private readonly GameProfile _p = ProfileLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "_example.json")));

    private (ProfileApplier Applier, PathResolver Resolver, Journal Journal) Build()
    {
        var resolver = new PathResolver(_fs, p => p.Replace("%LOCALAPPDATA%", "C:/Users/u/AppData/Local"));
        var j = new Journal(_fs, "C:/BR/j");
        return (new ProfileApplier(resolver, new SafeFileEditor(_fs, j, "C:/BR/b")), resolver, j);
    }

    [Fact]
    public void Resolve_ExpandsEnv_AndWildcardFolder()
    {
        _fs.Put($"{Gus}/acc1-eu/WindowsClient/GameUserSettings.ini", Ini);
        Assert.Single(Build().Resolver.Resolve(_p.Configs[0]));
    }

    [Fact]
    public void Resolve_PickNewest_ReturnsOnlyNewest()
    {
        _fs.Put($"{Gus}/acc1/WindowsClient/GameUserSettings.ini", Ini, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        _fs.Put($"{Gus}/acc2/WindowsClient/GameUserSettings.ini", Ini, new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));
        var r = Build().Resolver.Resolve(_p.Configs[0]);
        Assert.Single(r);
        Assert.Contains("acc2", r[0]);
    }

    [Fact] public void Resolve_NoMatch_ReturnsEmpty() => Assert.Empty(Build().Resolver.Resolve(_p.Configs[0]));

    [Fact]
    public void Apply_ChangesOnlyProfileKeys_PreservesRest()
    {
        var path = $"{Gus}/acc/WindowsClient/GameUserSettings.ini";
        _fs.Put(path, Ini);
        var report = Build().Applier.Apply(_p, BoostLevel.MaxFps, "op");
        Assert.True(report.Success);
        Assert.Equal(3, report.Targets.Single().ChangedKeys);
        Assert.Equal(Ini.Replace("=3", "=0"), _fs.Text(path));
    }

    [Fact]
    public void Apply_MissingConfig_ReportsNotFound_WritesNothing()
    {
        var before = _fs.AllFiles.Count;
        var report = Build().Applier.Apply(_p, BoostLevel.MaxFps, "op");
        Assert.True(report.Success);
        Assert.Equal(TargetStatus.NotFound, report.Targets.Single().Status);
        Assert.Equal(before, _fs.AllFiles.Count);
    }

    [Fact]
    public void Apply_SecondFileFails_RevertsFirst()
    {
        var p = _p with { Configs = [_p.Configs[0] with { Pick = "all" }] };
        var a = $"{Gus}/a/WindowsClient/GameUserSettings.ini";
        var b = $"{Gus}/b/WindowsClient/GameUserSettings.ini";
        _fs.Put(a, Ini).Put(b, Ini);
        _fs.SetReadOnly(b, true);
        var report = Build().Applier.Apply(p, BoostLevel.MaxFps, "op");
        Assert.False(report.Success);
        Assert.True(report.RolledBack);
        Assert.Equal(Ini, _fs.Text(a));
        Assert.Equal(Ini, _fs.Text(b));
    }

    [Fact]
    public void Apply_ProtectedKeyInProfile_ThrowsBeforeAnyWrite()
    {
        var path = $"{Gus}/acc/WindowsClient/GameUserSettings.ini";
        _fs.Put(path, Ini);
        var levels = _p.Levels.ToDictionary();
        levels[BoostLevel.MaxFps] = levels[BoostLevel.MaxFps] with
        {
            Set = new Dictionary<string, string>(levels[BoostLevel.MaxFps].Set) { ["gus:/Script/ShooterGame.ShooterGameUserSettings/ResolutionSizeX"] = "1920" },
        };
        Assert.Throws<ProtectedSettingException>(() => Build().Applier.Apply(_p with { Levels = levels }, BoostLevel.MaxFps, "op"));
        Assert.Equal(Ini, _fs.Text(path));
    }

    [Fact]
    public void Apply_AlreadyApplied_NoWrite_ZeroChanged()
    {
        var path = $"{Gus}/acc/WindowsClient/GameUserSettings.ini";
        _fs.Put(path, Ini.Replace("=3", "=0"));
        var (applier, _, journal) = Build();
        var report = applier.Apply(_p, BoostLevel.MaxFps, "op");
        Assert.Equal(0, report.Targets.Single().ChangedKeys);
        Assert.Empty(journal.ReadAll());
    }

    [Fact]
    public void Apply_StretchMode_WritesExemptResolutionOnly()
    {
        var path = $"{Gus}/acc/WindowsClient/GameUserSettings.ini";
        _fs.Put(path, Ini);
        var guard = new NeverTouchGuard(_p.NeverTouch).WithExemptions(_p.StretchKeys!);
        var extra = new Dictionary<string, string> { ["gus:/Script/ShooterGame.ShooterGameUserSettings/ResolutionSizeX"] = "1280", ["gus:/Script/ShooterGame.ShooterGameUserSettings/ResolutionSizeY"] = "960" };
        var report = Build().Applier.Apply(_p, BoostLevel.Competitive, "op", extra, guard);
        Assert.True(report.Success);
        Assert.Contains("ResolutionSizeX=1280", _fs.Text(path));
    }
}

public class ScoringTests
{
    [Fact]
    public void Score_ReferencePc()
    {
        var s = PcScorer.Score(new PcSpecs(55, 60, 32, 0.80, true, 13, 2));
        Assert.Equal((85, 100, 60, 64), (s.Ram, s.Disk, s.Health, s.Total));
    }

    [Fact]
    public void Score_ClampsInputs()
    {
        var s = PcScorer.Score(new PcSpecs(150, -20, 64, 1, false, 5, 9));
        Assert.Equal((100, 0, 100, 10, 0), (s.Gpu, s.Cpu, s.Ram, s.Disk, s.Health));
    }

    private static GameProfile P(params RecommendRule[] rules) => ProfileLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "_example.json"))) with { Recommend = rules };

    [Fact] public void Recommend_FirstMatchingRuleWins() =>
        Assert.Equal(BoostLevel.MaxFps, Recommender.Recommend(P(new(40, BoostLevel.MaxFps), new(60, BoostLevel.Balanced), new(null, BoostLevel.Competitive)), 30));

    [Fact] public void Recommend_FallsBackToDefault() =>
        Assert.Equal(BoostLevel.Competitive, Recommender.Recommend(P(new(40, BoostLevel.MaxFps), new(null, BoostLevel.Competitive)), 72));
}
