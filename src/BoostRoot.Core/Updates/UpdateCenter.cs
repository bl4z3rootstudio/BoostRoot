using System.Text.Json;

namespace BoostRoot.Core.Updates;

/// <summary>An app with a newer version available through winget.</summary>
public sealed record AppUpdate(string Name, string Id, string Current, string Available, string Source);

/// <summary>A pending Windows Update item (includes drivers: Type 2).</summary>
/// <param name="Optional">Not auto-selected by Windows (optional/preview) — BoostRoot marks it "skip if you like".</param>
public sealed record WinUpdate(string Id, string Title, bool IsDriver, bool Optional, long SizeBytes, string Kb)
{
    public bool LooksUnneeded => Optional && (Title.Contains("Preview", StringComparison.OrdinalIgnoreCase) || Title.Contains("Önizleme", StringComparison.OrdinalIgnoreCase));
}

/// <summary>A preinstalled store app the user may not want.</summary>
public sealed record BloatApp(string PackageFullName, string Name, string Friendly);

public static class WingetTable
{
    /// <summary>Parses `winget upgrade` table output in any language using column positions from the header row.</summary>
    public static IReadOnlyList<AppUpdate> Parse(string output)
    {
        var lines = output.Replace("\r", "", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd()).ToList();
        // winget prints progress spinners with \b and cursor codes before the table; keep text after the last one.
        for (var i = 0; i < lines.Count; i++)
        {
            var b = lines[i].LastIndexOfAny(['\b', '\u001b']);
            if (b >= 0) lines[i] = lines[i][(b + 1)..];
        }
        var dash = lines.FindIndex(l => l.Length > 10 && l.TrimStart().StartsWith("---", StringComparison.Ordinal) && l.Trim().All(c => c == '-'));
        if (dash < 1) return [];
        var header = lines[dash - 1];
        var starts = new List<int> { 0 };
        for (var i = 1; i < header.Length; i++)
            if (header[i] != ' ' && header[i - 1] == ' ') starts.Add(i);
        if (starts.Count < 4) return [];
        var res = new List<AppUpdate>();
        foreach (var l in lines.Skip(dash + 1))
        {
            if (l.Length < starts[^1] || string.IsNullOrWhiteSpace(l)) break; // footer ("N upgrades available.")
            string Col(int c)
            {
                var a = Math.Min(starts[c], l.Length);
                var e = c + 1 < starts.Count ? Math.Min(starts[c + 1], l.Length) : l.Length;
                return l[a..e].Trim();
            }
            var name = Col(0); var id = Col(1); var cur = Col(2); var avail = Col(3);
            var src = starts.Count > 4 ? Col(4) : "winget";
            if (id.Length == 0 || avail.Length == 0 || id.Contains(' ', StringComparison.Ordinal)) continue;
            res.Add(new AppUpdate(name.Replace("…", "", StringComparison.Ordinal), id, cur, avail, src));
        }
        return res;
    }
}

public static class WindowsUpdateJson
{
    /// <summary>PowerShell script (Windows Update Agent COM) that lists pending updates as JSON.</summary>
    public const string SearchScript = """
        $ErrorActionPreference='Stop'
        $s = New-Object -ComObject Microsoft.Update.Session
        $r = $s.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0")
        $out = foreach ($u in $r.Updates) { [pscustomobject]@{ Id=$u.Identity.UpdateID; Title=$u.Title; Type=[int]$u.Type; Auto=[bool]$u.AutoSelectOnWebSites; Size=[long]$u.MaxDownloadSize; Kb=(($u.KBArticleIDs | Select-Object -First 1) -as [string]) } }
        ConvertTo-Json -Compress -InputObject @($out)
        """;

    /// <summary>Downloads + installs the given update ids. Prints "RESULT:&lt;code&gt;;REBOOT:&lt;bool&gt;".</summary>
    public static string InstallScript(IEnumerable<string> ids) => $$"""
        $ErrorActionPreference='Stop'
        $want = @({{string.Join(",", ids.Where(i => Guid.TryParse(i, out _)).Select(i => "'" + i + "'"))}})
        $s = New-Object -ComObject Microsoft.Update.Session
        $r = $s.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0")
        $c = New-Object -ComObject Microsoft.Update.UpdateColl
        foreach ($u in $r.Updates) { if ($want -contains $u.Identity.UpdateID) { if (-not $u.EulaAccepted) { $u.AcceptEula() }; [void]$c.Add($u) } }
        if ($c.Count -eq 0) { 'RESULT:0;REBOOT:False'; exit }
        $d = $s.CreateUpdateDownloader(); $d.Updates = $c; [void]$d.Download()
        $i = $s.CreateUpdateInstaller(); $i.Updates = $c; $res = $i.Install()
        "RESULT:$($res.ResultCode);REBOOT:$($res.RebootRequired)"
        """;

    /// <summary>Hides one update so Windows stops offering it (user chose "skip").</summary>
    public static string HideScript(string id) => Guid.TryParse(id, out _) ? $$"""
        $s = New-Object -ComObject Microsoft.Update.Session
        $r = $s.CreateUpdateSearcher().Search("IsInstalled=0")
        foreach ($u in $r.Updates) { if ($u.Identity.UpdateID -eq '{{id}}') { $u.IsHidden = $true } }
        'OK'
        """ : "'BAD'";

    public static IReadOnlyList<WinUpdate> Parse(string json)
    {
        var list = new List<WinUpdate>();
        try
        {
            using var doc = JsonDocument.Parse(json.Trim().Length == 0 ? "[]" : json);
            var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            foreach (var e in arr)
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var type = e.TryGetProperty("Type", out var t) && t.TryGetInt32(out var ti) ? ti : 1;
                var auto = e.TryGetProperty("Auto", out var a) && a.ValueKind == JsonValueKind.True;
                var size = e.TryGetProperty("Size", out var z) && z.TryGetInt64(out var zl) ? zl : 0;
                list.Add(new WinUpdate(S("Id"), S("Title"), type == 2, !auto, size, S("Kb")));
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>"RESULT:2;REBOOT:True" → (succeeded, reboot). ResultCode 2 = Succeeded, 3 = SucceededWithErrors.</summary>
    public static (bool Ok, bool Reboot) ParseInstall(string output)
    {
        var line = output.Split('\n').LastOrDefault(l => l.StartsWith("RESULT:", StringComparison.Ordinal))?.Trim() ?? "";
        var ok = line.StartsWith("RESULT:2", StringComparison.Ordinal) || line.StartsWith("RESULT:3", StringComparison.Ordinal) || line.StartsWith("RESULT:0", StringComparison.Ordinal);
        return (ok, line.Contains("REBOOT:True", StringComparison.OrdinalIgnoreCase));
    }
}

public static class Bloatware
{
    /// <summary>Package name prefixes of preinstalled apps that are safe to remove (they can be reinstalled from the Store).</summary>
    public static IReadOnlyList<(string Prefix, string Tr, string En)> Known { get; } =
    [
        ("king.com.", "Candy Crush ve diğer King oyunları", "Candy Crush and other King games"),
        ("Microsoft.BingNews", "Microsoft Haberler", "Microsoft News"),
        ("Microsoft.BingWeather", "Hava Durumu", "Weather"),
        ("Microsoft.BingSearch", "Bing Arama", "Bing Search"),
        ("Microsoft.GetHelp", "Yardım Alın", "Get Help"),
        ("Microsoft.Getstarted", "İpuçları", "Tips"),
        ("Microsoft.MicrosoftSolitaireCollection", "Solitaire Koleksiyonu", "Solitaire Collection"),
        ("Microsoft.MicrosoftOfficeHub", "Office (reklam uygulaması)", "Office hub"),
        ("Microsoft.People", "Kişiler", "People"),
        ("Microsoft.SkypeApp", "Skype", "Skype"),
        ("Microsoft.Todos", "Microsoft To Do", "Microsoft To Do"),
        ("Microsoft.WindowsFeedbackHub", "Geri Bildirim Merkezi", "Feedback Hub"),
        ("Microsoft.WindowsMaps", "Haritalar", "Maps"),
        ("Microsoft.ZuneVideo", "Filmler ve TV", "Movies & TV"),
        ("Microsoft.549981C3F5F10", "Cortana", "Cortana"),
        ("Clipchamp.Clipchamp", "Clipchamp", "Clipchamp"),
        ("Microsoft.PowerAutomateDesktop", "Power Automate", "Power Automate"),
        ("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family", "Microsoft Family"),
        ("Microsoft.MixedReality.Portal", "Karma Gerçeklik Portalı", "Mixed Reality Portal"),
        ("Microsoft.Microsoft3DViewer", "3D Görüntüleyici", "3D Viewer"),
        ("Microsoft.WindowsAlarms", "Saat ve Alarmlar", "Clock & Alarms"),
        ("BytedancePte.Ltd.TikTok", "TikTok", "TikTok"),
        ("Facebook.", "Facebook / Instagram", "Facebook / Instagram"),
        ("Disney.", "Disney+", "Disney+"),
        ("AmazonVideo.PrimeVideo", "Prime Video", "Prime Video"),
        ("5A894077.McAfeeSecurity", "McAfee (deneme)", "McAfee (trial)"),
        ("Microsoft.MicrosoftJournal", "Journal", "Journal"),
        ("Microsoft.OutlookForWindows", "Yeni Outlook", "New Outlook"),
        ("MSTeams", "Teams (kişisel)", "Teams (personal)"),
    ];

    /// <summary>Never removed: gaming, store, winget and frameworks.</summary>
    public static readonly string[] Protected = ["Microsoft.Xbox", "Microsoft.GamingApp", "Microsoft.GamingServices", "Microsoft.WindowsStore", "Microsoft.DesktopAppInstaller", "Microsoft.VCLibs", "Microsoft.UI.Xaml", "Microsoft.NET", "Microsoft.StorePurchaseApp", "Microsoft.XboxGameOverlay", "Microsoft.XboxIdentityProvider", "Microsoft.SecHealthUI", "Microsoft.WindowsTerminal", "Microsoft.Windows.Photos", "Microsoft.WindowsCalculator", "Microsoft.WindowsNotepad", "Microsoft.Paint", "Microsoft.ScreenSketch"];

    public const string ListScript = "Get-AppxPackage | Select-Object Name,PackageFullName | ConvertTo-Json -Compress";
    public static string RemoveScript(string fullName) => fullName.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '~')
        ? $"Remove-AppxPackage -Package '{fullName}'; 'OK'" : "'BAD'";

    public static IReadOnlyList<BloatApp> Match(string json, bool turkish)
    {
        var res = new List<BloatApp>();
        try
        {
            using var doc = JsonDocument.Parse(json.Trim().Length == 0 ? "[]" : json);
            var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            foreach (var e in arr)
            {
                var name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                var full = e.TryGetProperty("PackageFullName", out var f) ? f.GetString() ?? "" : "";
                if (Protected.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                var k = Known.FirstOrDefault(k => name.StartsWith(k.Prefix, StringComparison.OrdinalIgnoreCase));
                if (k.Prefix is null || res.Any(r => r.Name == name)) continue;
                res.Add(new BloatApp(full, name, turkish ? k.Tr : k.En));
            }
        }
        catch (JsonException) { }
        return res;
    }
}
