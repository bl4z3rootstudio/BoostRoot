class_name GameDefs
## Per-game constants. Only change these from a verified source: they are what makes the aim feel identical.

const GAMES := {
	"valorant": {
		"name": "VALORANT",
		"yaw": 0.07,            # degrees per mouse count at sensitivity 1
		"hfov": 103.0,          # fixed horizontal FOV (all aspect ratios)
		"run": 5.4,             # m/s holding a rifle (6.75 with knife)
		"walk_mult": 0.6,       # Shift walk
		"crouch_mult": 0.5,
		"friction": 33.0,       # m/s² when no key: run -> stop in ~0.16 s (Riot's measured counter-strafe timing)
		"accel": 40.0,
		"deadzone": 0.25,       # at or below 25 % speed shots are fully accurate
		"eye": 1.6, "eye_crouch": 1.15,
		"head_y": 1.6, "head_r": 0.125,
		"hp": 150,
		"weapon": {"name": "Vandal", "rps": 9.75, "head": 160, "body": 40, "legs": 34, "mag": 25, "reload": 2.5,
			"first": 0.25, "walk_err": 1.1, "run_err": 5.0, "recover": 0.35},
		"default_sens": 0.4,
		"enabled": true,
	},
	"cs2": {"name": "COUNTER-STRIKE 2", "yaw": 0.022, "hfov": 106.26, "enabled": false, "default_sens": 1.25},
	"apex": {"name": "APEX LEGENDS", "yaw": 0.022, "hfov": 110.0, "enabled": false, "default_sens": 2.0},
	"ow2": {"name": "OVERWATCH 2", "yaw": 0.0066, "hfov": 103.0, "enabled": false, "default_sens": 6.0},
}

static func get_game(id: String) -> Dictionary:
	return GAMES.get(id, GAMES["valorant"])

## Centimetres of mouse travel for one full turn.
static func cm_per_360(game: String, sens: float, dpi: int) -> float:
	var g := get_game(game)
	if sens <= 0.0 or dpi <= 0:
		return 0.0
	return 360.0 / (sens * float(g["yaw"]) * dpi) * 2.54
