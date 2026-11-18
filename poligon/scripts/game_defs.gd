class_name GameDefs
## Per-game constants. Only change these from a verified source: they are what makes the aim feel identical.
## Distances in metres. "fov" is horizontal (fov_axis "h", fixed for every aspect ratio, like VALORANT)
## or vertical (fov_axis "v", Hor+ like CS2: wider screens see more).

const GAMES := {
	"valorant": {
		"name": "VALORANT",
		"yaw": 0.07,            # degrees per mouse count at sensitivity 1
		"fov": 103.0, "fov_axis": "h",
		"walk_mult": 0.6, "crouch_mult": 0.5,
		"friction": 33.0,       # run -> stop in ~0.16 s (Riot's measured counter-strafe timing)
		"accel": 40.0,
		"deadzone": 0.25,       # at or below 25 % speed shots are fully accurate
		"eye": 1.6, "eye_crouch": 1.15, "head_y": 1.6,
		"hp": 150,
		"enemy_run": 5.4,
		"default_sens": 0.4,
		"enabled": true,
		"default_weapon": "vandal",
		"secondary": "sheriff",
		"knife": {"name": "Bıçak", "auto": false, "rps": 0.0, "mag": 0, "reload": 0.0, "run": 6.75, "melee": true,
			"first": 0.0, "walk_err": 0.0, "run_err": 0.0, "recover": 0.0, "dmg": [[2, 50, 50, 50]], "equip": 0.5},
		"weapons": {
			"vandal": {"name": "Vandal", "equip": 1.0, "zoom": 1.25, "auto": true, "rps": 9.75, "mag": 25, "reload": 2.5, "run": 5.4,
				"first": 0.25, "walk_err": 1.1, "run_err": 5.0, "recover": 0.35,
				"dmg": [[50, 160, 40, 34]]},
			"phantom": {"name": "Phantom", "equip": 1.0, "zoom": 1.15, "auto": true, "rps": 11.0, "mag": 30, "reload": 2.5, "run": 5.4,
				"first": 0.2, "walk_err": 1.1, "run_err": 5.0, "recover": 0.3,
				"dmg": [[15, 156, 39, 33], [30, 140, 35, 29], [999, 124, 31, 26]]},
			"sheriff": {"name": "Sheriff", "equip": 0.75, "zoom": 1.25, "auto": false, "rps": 4.0, "mag": 6, "reload": 2.25, "run": 5.73,
				"first": 0.25, "walk_err": 1.5, "run_err": 4.0, "recover": 0.4,
				"dmg": [[30, 159, 55, 47], [999, 145, 50, 43]]},
		},
	},
	"cs2": {
		"name": "COUNTER-STRIKE 2",
		"yaw": 0.022,
		"fov": 73.74, "fov_axis": "v",   # fov 90 at 4:3 → 106.26° horizontal on 16:9
		"walk_mult": 0.52, "crouch_mult": 0.34,
		"friction": 42.0, "accel": 55.0,
		"deadzone": 0.34,                 # CS: accurate below 34 % of max speed
		"eye": 1.22, "eye_crouch": 0.88, "head_y": 1.22,   # 64 / 46 units × 0.01905 m
		"hp": 100,
		"enemy_run": 4.1,
		"default_sens": 1.25,
		"enabled": true,
		"default_weapon": "ak47",
		"secondary": "deagle",
		"knife": {"name": "Bıçak", "auto": false, "rps": 0.0, "mag": 0, "reload": 0.0, "run": 4.76, "melee": true,
			"first": 0.0, "walk_err": 0.0, "run_err": 0.0, "recover": 0.0, "dmg": [[2, 40, 40, 40]], "equip": 0.5},
		# Damage against a helmet + kevlar.
		"weapons": {
			"ak47": {"name": "AK-47", "equip": 1.0, "zoom": 1.0, "auto": true, "rps": 10.0, "mag": 30, "reload": 2.4, "run": 4.1,
				"first": 0.15, "walk_err": 2.0, "run_err": 6.0, "recover": 0.38, "dmg": [[999, 111, 27, 27]]},
			"m4a1s": {"name": "M4A1-S", "equip": 1.0, "zoom": 1.0, "auto": true, "rps": 10.0, "mag": 20, "reload": 3.1, "run": 4.29,
				"first": 0.12, "walk_err": 1.8, "run_err": 5.5, "recover": 0.32, "dmg": [[999, 106, 26, 29]]},
			"deagle": {"name": "Desert Eagle", "equip": 0.9, "zoom": 1.0, "auto": false, "rps": 3.7, "mag": 7, "reload": 2.2, "run": 4.38,
				"first": 0.3, "walk_err": 2.5, "run_err": 6.0, "recover": 0.5, "dmg": [[999, 197, 49, 39]]},
		},
	},
	"apex": {"name": "APEX LEGENDS", "yaw": 0.022, "enabled": false, "default_sens": 2.0},
	"ow2": {"name": "OVERWATCH 2", "yaw": 0.0066, "enabled": false, "default_sens": 6.0},
}

static func get_game(id: String) -> Dictionary:
	return GAMES.get(id, GAMES["valorant"])

## Damage of one bullet for a hit part at a distance (range falloff like the game).
static func damage(w: Dictionary, part: String, dist: float) -> int:
	var idx := 1 if part == "head" else (2 if part == "body" else 3)
	for row in w["dmg"]:
		if dist <= float(row[0]):
			return int(row[idx])
	return int(w["dmg"][-1][idx])

## Centimetres of mouse travel for one full turn.
static func cm_per_360(game: String, sens: float, dpi: int) -> float:
	var g := get_game(game)
	if sens <= 0.0 or dpi <= 0:
		return 0.0
	return 360.0 / (sens * float(g["yaw"]) * dpi) * 2.54
