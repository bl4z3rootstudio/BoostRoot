using System.Text;
using System.Text.Json.Nodes;
using BoostRoot.Core;
using BoostRoot.Core.Config;
using BoostRoot.Core.IO;
using BoostRoot.Core.Safety;
using BoostRoot.Core.Versioning;

namespace BoostRoot.Core.Tests;

public class SmokeTests
{
    [Fact] public void Core_Assembly_Loads() => Assert.NotNull(typeof(AssemblyMarker).Assembly);
}

public class AppVersionTests
{
    [Theory]
    [InlineData("26.11.1", 26, 11, 1)]
    [InlineData("v26.12.3", 26, 12, 3)]
    [InlineData(" 27.1.1 ", 27, 1, 1)]
    public void TryParse_Valid(string s, int y, int m, int r)
    {
        Assert.True(AppVersion.TryParse(s, out var v));
        Assert.Equal(new AppVersion(y, m, r), v);
    }

    [Theory]
    [InlineData("26.13.1")] [InlineData("26.0.1")] [InlineData("26.11.0")] [InlineData("1.26.11")]
    [InlineData("26.11")] [InlineData("abc")] [InlineData("")] [InlineData(null)] [InlineData("26.11.1.2")] [InlineData("26.-1.1")]
    public void TryParse_Invalid(string? s) => Assert.False(AppVersion.TryParse(s, out _));

    [Fact]
    public void Ordering_YearThenMonthThenRelease()
    {
        var list = new[] { "27.1.1", "26.11.2", "26.12.1", "26.11.1" }.Select(AppVersion.Parse).Order().Select(v => v.ToString());
        Assert.Equal(["26.11.1", "26.11.2", "26.12.1", "27.1.1"], list);
    }

    [Fact] public void FileVersion() => Assert.Equal("26.11.1.0", new AppVersion(26, 11, 1).ToFileVersion());
    [Fact] public void Current_IsBuildVersion() => Assert.Equal("26.11.2", AppVersion.Current.ToString());
}

public class TextFileTests
{
    [Fact]
    public void TextFile_RoundTrip_PreservesBomAndCrlf()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a=1\r\nb=2\r\n")];
        var f = TextFile.Decode(bytes);
        Assert.Equal("a=1\nb=2\n", f.Text);
        Assert.Equal("\r\n", f.Newline);
        Assert.True(f.Utf8Bom);
        Assert.Equal(bytes, f.Encode());
    }

    [Fact]
    public void TextFile_NoBom_Lf_RoundTrip()
    {
        var bytes = Encoding.UTF8.GetBytes("x:ğüş\ny:2\n");
        var f = TextFile.Decode(bytes);
        Assert.False(f.Utf8Bom);
        Assert.Equal("\n", f.Newline);
        Assert.Equal(bytes, f.Encode());
    }

    [Fact]
    public void PhysicalFileSystem_WriteAtomic_ReadOnlyTarget_Throws_AndKeepsContent()
    {
        var dir = Directory.CreateTempSubdirectory("br");
        var path = Path.Combine(dir.FullName, "a.ini");
        File.WriteAllText(path, "orig");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead);
        try
        {
            Assert.True(PhysicalFileSystem.Instance.IsReadOnly(path));
            Assert.Throws<UnauthorizedAccessException>(() => PhysicalFileSystem.Instance.WriteAllBytesAtomic(path, [1, 2]));
            Assert.Equal("orig", File.ReadAllText(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            dir.Delete(true);
        }
    }

    [Fact]
    public void PhysicalFileSystem_WriteAtomic_Writes()
    {
        var dir = Directory.CreateTempSubdirectory("br");
        var path = Path.Combine(dir.FullName, "sub", "b.txt");
        PhysicalFileSystem.Instance.WriteAllBytesAtomic(path, [65]);
        Assert.Equal("A", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.brtmp", SearchOption.AllDirectories));
        dir.Delete(true);
    }

    [Fact]
    public void InMemory_EnumerateFiles_Wildcard()
    {
        var fs = new Testing.InMemoryFileSystem().Put("C:/g/a.ini", "").Put("C:/g/s/b.ini", "").Put("C:/g/c.txt", "");
        Assert.Single(fs.EnumerateFiles("C:/g", "*.ini", false));
        Assert.Equal(2, fs.EnumerateFiles("C:/g", "*.ini", true).Count());
    }
}

public class ConfigFormatTests
{
    private static IConfigDocument Ini(string t) => ConfigFormats.Get("ini").Parse(t);

    [Fact]
    public void Ini_Get_Set_RoundTrip()
    {
        var d = Ini("[A]\nx=1\n; yorum\n[B]\ny=2\n");
        Assert.Equal("1", d.Get("A/x"));
        d.Set("B/y", "5");
        Assert.Equal("[A]\nx=1\n; yorum\n[B]\ny=5\n", d.Serialize());
    }

    [Fact]
    public void Ini_Set_MissingKey_AppendsInSection()
    {
        var d = Ini("[A]\nx=1\n\n[B]\ny=2\n");
        d.Set("A/z", "3");
        Assert.Equal("[A]\nx=1\nz=3\n\n[B]\ny=2\n", d.Serialize());
    }

    [Fact]
    public void Ini_Set_MissingSection_AppendsAtEnd()
    {
        var d = Ini("[A]\nx=1\n");
        d.Set("/Script/Engine.X/k", "v");
        Assert.Equal("[A]\nx=1\n\n[/Script/Engine.X]\nk=v\n", d.Serialize());
        Assert.Equal("v", d.Get("/Script/Engine.X/k"));
    }

    [Fact]
    public void Ini_PreservesArrayAndMalformedLines()
    {
        const string input = "[S]\n+Paths=a\n-Paths=b\nbozuk satır\nk=v\n[S]\nk=ikinci\n";
        var d = Ini(input);
        Assert.Equal(["S/k"], d.Keys);
        d.Set("S/k", "z");
        Assert.Equal(input.Replace("k=v", "k=z"), d.Serialize());
    }

    [Fact]
    public void Ini_Set_KeepsCrlf()
    {
        var f = TextFile.Decode(Encoding.UTF8.GetBytes("[A]\r\nx = 1\r\n"));
        var d = Ini(f.Text);
        d.Set("A/x", "2");
        Assert.Equal("[A]\r\nx = 2\r\n", Encoding.UTF8.GetString(f.WithText(d.Serialize()).Encode()));
    }

    [Fact]
    public void KeyValue_Colon_Minecraft()
    {
        var d = ConfigFormats.Get("keyvalue", ":").Parse("renderDistance:12\ngraphicsMode:1\n");
        d.Set("renderDistance", "8");
        Assert.Equal("renderDistance:8\ngraphicsMode:1\n", d.Serialize());
        d.Set("newKey", "x");
        Assert.Equal("renderDistance:8\ngraphicsMode:1\nnewKey:x\n", d.Serialize());
    }

    [Fact]
    public void KeyValues_NestedPath()
    {
        const string input = "\"video.cfg\"\n{\n\t\"setting.mat_vsync\"\t\t\"1\"\n}\n";
        var d = ConfigFormats.Get("keyvalues").Parse(input);
        Assert.Equal("1", d.Get("video.cfg/setting.mat_vsync"));
        d.Set("video.cfg/setting.mat_vsync", "0");
        Assert.Equal(input.Replace("\"1\"", "\"0\""), d.Serialize());
        d.Set("video.cfg/setting.new", "2");
        Assert.Equal("2", d.Get("video.cfg/setting.new"));
        Assert.Throws<ArgumentException>(() => d.Set("nope/x", "1"));
    }

    [Fact]
    public void Json_TypePreserved()
    {
        var d = ConfigFormats.Get("json").Parse("{\"a\":{\"b\":1,\"c\":true,\"d\":\"x\"}}");
        d.Set("a/b", "3"); d.Set("a/c", "false"); d.Set("a/d", "y"); d.Set("a/e/f", "2");
        Assert.Equal("3", d.Get("a/b")); Assert.Equal("false", d.Get("a/c")); Assert.Equal("y", d.Get("a/d"));
        var n = JsonNode.Parse(d.Serialize())!;
        Assert.Equal(System.Text.Json.JsonValueKind.Number, n["a"]!["b"]!.GetValueKind());
        Assert.Equal(System.Text.Json.JsonValueKind.False, n["a"]!["c"]!.GetValueKind());
        Assert.Equal(System.Text.Json.JsonValueKind.String, n["a"]!["d"]!.GetValueKind());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, n["a"]!["e"]!["f"]!.GetValueKind());
        Assert.Contains("a/b", d.Keys);
    }

    [Fact]
    public void Xml_ElementAndAttribute()
    {
        const string input = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Settings>\n  <shadow value=\"3\" />\n  <fps>60</fps>\n</Settings>\n";
        var d = ConfigFormats.Get("xml").Parse(input);
        Assert.Equal("3", d.Get("Settings/shadow@value"));
        d.Set("Settings/shadow@value", "0");
        d.Set("Settings/fps", "0");
        var s = d.Serialize();
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Settings>\n  <shadow value=\"0\" />", s);
        Assert.Contains("<fps>0</fps>", s);
        Assert.EndsWith("</Settings>\n", s);
    }

    [Fact] public void ConfigFormats_Unknown_Throws() => Assert.Throws<ArgumentException>(() => ConfigFormats.Get("yaml"));
}

public class NeverTouchGuardTests
{
    private static readonly NeverTouchGuard G = new([]);

    [Fact] public void BuiltIn_Blocks_Resolution() => Assert.True(G.IsProtected("gus", "/Script/ShooterGame.ShooterGameUserSettings/ResolutionSizeX"));
    [Fact] public void BuiltIn_Blocks_Sensitivity_CaseInsensitive() => Assert.True(G.IsProtected("x", "Mouse/MouseSensitivity"));
    [Fact] public void BuiltIn_Blocks_Volume_And_Binding() { Assert.True(G.IsProtected("x", "Audio/MasterVolume")); Assert.True(G.IsProtected("x", "Input/KeyBindings")); }
    [Fact] public void BuiltIn_Allows_ShadowQuality() => Assert.False(G.IsProtected("gus", "ScalabilityGroups/sg.ShadowQuality"));
    [Fact] public void BuiltIn_Allows_Volumetric_And_ShadowResolution() { Assert.False(G.IsProtected("x", "Graphics/VolumetricFog")); Assert.False(G.IsProtected("x", "Graphics/ShadowMapResolution")); }

    [Fact]
    public void ProfilePattern_Blocks_SpecificKey()
    {
        var g = new NeverTouchGuard(["opt:guiScale"]);
        Assert.True(g.IsProtected("opt", "guiScale"));
        Assert.False(g.IsProtected("other", "guiScale"));
    }

    [Fact]
    public void EnsureAllowed_Throws_WithKeyInMessage()
    {
        var ex = Assert.Throws<ProtectedSettingException>(() => G.EnsureAllowed("gus", "Settings/ResolutionSizeX"));
        Assert.Contains("ResolutionSizeX", ex.Message);
    }

    [Fact]
    public void WithExemptions_AllowsOnlyListedKey()
    {
        var g = G.WithExemptions(["gus:Settings/ResolutionSizeX"]);
        Assert.False(g.IsProtected("gus", "Settings/ResolutionSizeX"));
        Assert.True(g.IsProtected("gus", "Settings/ResolutionSizeY"));
    }
}
