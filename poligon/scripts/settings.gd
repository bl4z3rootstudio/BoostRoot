extends Node
## Poligon settings + result history. Stored as JSON next to BoostRoot's data when launched from BoostRoot
## (--data=<folder>), otherwise in the Godot user folder.

const DIFFICULTY := {"easy": 1500, "normal": 1000, "hard": 700, "pro": 500}

var data_dir := "user://"
var data := {
	"game": "valorant",
	"sens": {"valorant": 0.4},
	"weapon": {},
	"dpi": 800,
	"stretched": false,
	"stretch_w": 1440,
	"stretch_h": 1080,
	"xhair": "0;P;c;1;o;1;0t;1;0l;4;0o;2;0a;1;1b;0",
	"volume": 0.7,
	"difficulty": "normal",
	"react_ms": 1000,
	"bot_speed": 1.0,
	"duration": 60,
	"headshot_only": false,
	"survival": true,
	"fps_cap": 0,
	"vsync": false,
	"best": {},
}
var selftest := false

func _ready() -> void:
	var overrides := {}
	for a in OS.get_cmdline_user_args():
		if a == "--selftest":
			selftest = true
		elif a.begins_with("--") and a.contains("="):
			var kv := a.substr(2).split("=", true, 1)
			overrides[kv[0]] = kv[1]
	if overrides.has("data"):
		data_dir = String(overrides["data"]).replace("\\", "/")
		if not data_dir.ends_with("/"):
			data_dir += "/"
		DirAccess.make_dir_recursive_absolute(data_dir)
	if not selftest:
		load_settings()
	# BoostRoot passes the player's real sensitivity / crosshair code on launch.
	if overrides.has("game") and GameDefs.GAMES.has(String(overrides["game"])) and GameDefs.get_game(String(overrides["game"])).get("enabled", false):
		data["game"] = overrides["game"]
	if overrides.has("sens") and String(overrides["sens"]).replace(",", ".").is_valid_float():
		set_sens(String(overrides["sens"]).replace(",", ".").to_float())
	if overrides.has("dpi") and String(overrides["dpi"]).is_valid_int():
		data["dpi"] = int(overrides["dpi"])
	if overrides.has("xhair") and Crosshair.parse(String(overrides["xhair"])) != null:
		data["xhair"] = overrides["xhair"]
	if overrides.has("stretched"):
		data["stretched"] = overrides["stretched"] == "1" or overrides["stretched"] == "true"
	apply_display()

func path(file: String) -> String:
	return data_dir + file

func load_settings() -> void:
	if not FileAccess.file_exists(path("settings.json")):
		return
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path("settings.json")))
	if parsed is Dictionary:
		for k in parsed.keys():
			data[k] = parsed[k]

func save() -> void:
	if selftest:
		return
	var f := FileAccess.open(path("settings.json"), FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(data, "\t"))

func game() -> Dictionary:
	return GameDefs.get_game(String(data["game"]))

func sens() -> float:
	var s: Dictionary = data["sens"]
	return float(s.get(String(data["game"]), game().get("default_sens", 1.0)))

func set_sens(v: float) -> void:
	if v <= 0.0 or v > 100.0:
		return
	var s: Dictionary = data["sens"]
	s[String(data["game"])] = v
	data["sens"] = s

## Degrees turned per raw mouse count — identical to the game.
func deg_per_count() -> float:
	return sens() * float(game()["yaw"])

func weapon_id() -> String:
	var w: Dictionary = data["weapon"]
	var id := String(w.get(String(data["game"]), game().get("default_weapon", "")))
	return id if (game()["weapons"] as Dictionary).has(id) else String(game()["default_weapon"])

func weapon() -> Dictionary:
	var w: Dictionary = game()["weapons"][weapon_id()]
	return w

func set_weapon(id: String) -> void:
	var w: Dictionary = data["weapon"]
	w[String(data["game"])] = id
	data["weapon"] = w

func react_ms() -> int:
	return int(data["react_ms"])

func apply_display() -> void:
	var root := get_tree().root
	if bool(data["stretched"]):
		# Render at e.g. 1440x1080 and stretch to the screen exactly like "stretched 4:3" in VALORANT.
		root.content_scale_mode = Window.CONTENT_SCALE_MODE_VIEWPORT
		root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_IGNORE
		root.content_scale_size = Vector2i(int(data["stretch_w"]), int(data["stretch_h"]))
	else:
		root.content_scale_mode = Window.CONTENT_SCALE_MODE_DISABLED
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_ENABLED if bool(data["vsync"]) else DisplayServer.VSYNC_DISABLED)
	Engine.max_fps = int(data["fps_cap"])
	Input.use_accumulated_input = false
	AudioServer.set_bus_volume_db(0, linear_to_db(clampf(float(data["volume"]), 0.0001, 1.0)))

## Appends a finished run to results.json (BoostRoot reads it for the history chart) and updates the best score.
func add_result(r: Dictionary) -> bool:
	var key := String(data["game"]) + ":" + String(r["drill"])
	var best: Dictionary = data["best"]
	var is_best := float(r["score"]) > float(best.get(key, -1.0))
	if is_best:
		best[key] = r["score"]
		data["best"] = best
	if selftest:
		return is_best
	var list := []
	if FileAccess.file_exists(path("results.json")):
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(path("results.json")))
		if parsed is Array:
			list = parsed
	list.append(r)
	while list.size() > 1000:
		list.pop_front()
	var f := FileAccess.open(path("results.json"), FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(list))
	save()
	return is_best

func best(drill: String) -> float:
	return float((data["best"] as Dictionary).get(String(data["game"]) + ":" + drill, -1.0))
