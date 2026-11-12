using BoostRoot.Core.Config;
using BoostRoot.Core.IO;
using BoostRoot.Core.Profiles;
using BoostRoot.Core.Versioning;

// Exercises the reflection-sensitive paths (JSON profiles, regex, config formats) under NativeAOT.
var path = args.Length > 0 ? args[0] : Path.Combine("profiles", "_example.json");
var profile = ProfileLoader.Parse(TextFile.Decode(File.ReadAllBytes(path)).Text);
var errors = ProfileValidator.Validate(profile);
foreach (var e in errors) Console.Error.WriteLine(e);
var json = ConfigFormats.Get("json").Parse("{\"a\":1}");
json.Set("a", "2");
var xml = ConfigFormats.Get("xml").Parse("<r><a>1</a></r>");
xml.Set("r/a", "2");
var kv = ConfigFormats.Get("keyvalues").Parse("\"b\"\n{\n\t\"k\"\t\"1\"\n}\n");
kv.Set("b/k", "2");
Console.WriteLine($"BoostRoot {AppVersion.Current} AOT probe: {profile.Id} levels={profile.Levels.Count} json={json.Get("a")} xml={xml.Get("r/a")} kv={kv.Get("b/k")}");
return errors.Count == 0 && json.Get("a") == "2" && xml.Get("r/a") == "2" && kv.Get("b/k") == "2" ? 0 : 1;
