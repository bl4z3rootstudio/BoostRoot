class_name Crosshair
extends Control
## Draws a VALORANT crosshair from its profile code (Settings → Crosshair → Export profile code in the game).
## Sizes are VALORANT pixels at 1080p and scale with the render height, like the game.

const COLORS := ["FFFFFF", "00FF00", "7FFF00", "DFFF00", "FFFF00", "00FFFF", "FF00FF", "FF0000"]

var spec: Dictionary = {}
## Degrees of current movement / firing error (spreads the lines that have those options on).
var move_error := 0.0
var fire_error := 0.0

static func parse(code: String) -> Variant:
	var parts := code.strip_edges().split(";")
	if parts.size() < 2 or parts[0] != "0":
		return null
	var d := {}
	var section := ""
	var i := 1
	while i < parts.size():
		var p := parts[i]
		if p == "P" or p == "A" or p == "S":
			section = p
			i += 1
			continue
		if i + 1 >= parts.size():
			break
		if section == "P":
			d[p] = parts[i + 1]
		i += 2
	var c := _i(d, "c", 0)
	var color := Color.WHITE
	if c == 8 and d.has("u") and String(d["u"]).length() >= 6:
		color = Color.html("#" + String(d["u"]).substr(0, 6))
	elif c >= 0 and c < COLORS.size():
		color = Color.html("#" + COLORS[c])
	return {
		"color": color,
		"outline": _i(d, "h", 1) == 1, "outline_t": _i(d, "t", 1), "outline_a": _f(d, "o", 0.5),
		"dot": _i(d, "d", 0) == 1, "dot_t": _i(d, "z", 2), "dot_a": _f(d, "a", 1.0),
		"inner": {"show": _i(d, "0b", 1) == 1, "t": _i(d, "0t", 2), "l": _i(d, "0l", 6), "v": _i(d, "0v", _i(d, "0l", 6)) if _i(d, "0g", 0) == 1 else _i(d, "0l", 6),
			"o": _i(d, "0o", 3), "a": _f(d, "0a", 0.8), "m": _i(d, "0m", 0) == 1, "f": _i(d, "0f", 1) == 1},
		"outer": {"show": _i(d, "1b", 1) == 1, "t": _i(d, "1t", 2), "l": _i(d, "1l", 2), "v": _i(d, "1v", _i(d, "1l", 2)) if _i(d, "1g", 0) == 1 else _i(d, "1l", 2),
			"o": _i(d, "1o", 10), "a": _f(d, "1a", 0.35), "m": _i(d, "1m", 1) == 1, "f": _i(d, "1f", 1) == 1},
	}

static func _i(d: Dictionary, k: String, def: int) -> int:
	return int(d[k]) if d.has(k) and String(d[k]).is_valid_int() else def

static func _f(d: Dictionary, k: String, def: float) -> float:
	return float(d[k]) if d.has(k) and String(d[k]).is_valid_float() else def

func set_code(code: String) -> void:
	var s = parse(code)
	if s != null:
		spec = s
		queue_redraw()

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	resized.connect(queue_redraw)

func _draw() -> void:
	if spec.is_empty():
		return
	var k := get_viewport_rect().size.y / 1080.0
	# Centre of this control (the HUD covers the screen; the menu preview is a small panel).
	var area := size if size.x > 1.0 and size.y > 1.0 else get_viewport_rect().size
	var c := area * 0.5
	c = Vector2(floor(c.x), floor(c.y))
	var col: Color = spec["color"]
	for key in ["outer", "inner"]:
		var l: Dictionary = spec[key]
		if not l["show"] or int(l["l"]) <= 0 or int(l["t"]) <= 0:
			continue
		var extra := 0.0
		if l["m"]:
			extra += move_error * 4.0
		if l["f"]:
			extra += fire_error * 4.0
		# Whole pixels only (VALORANT draws crisp; half pixels would blur a 1 px line into two faint ones).
		var t := maxf(1.0, roundf(float(l["t"]) * k))
		var o := roundf((float(l["o"]) + extra) * k)
		var hl := maxf(1.0, roundf(float(l["l"]) * k))
		var vl := maxf(1.0, roundf(float(l["v"]) * k))
		var a := float(l["a"])
		var h0 := floorf(t / 2.0)   # pixels above/left of the centre row/column
		# right, left, down, up — the centre pixel itself is (c.x, c.y)
		_bar(Rect2(c.x + 1.0 + o, c.y - h0, hl, t), col, a, k)
		_bar(Rect2(c.x - o - hl, c.y - h0, hl, t), col, a, k)
		_bar(Rect2(c.x - h0, c.y + 1.0 + o, t, vl), col, a, k)
		_bar(Rect2(c.x - h0, c.y - o - vl, t, vl), col, a, k)
		if o <= 0.0:
			_bar(Rect2(c.x - h0, c.y - h0, t, t), col, a, k)   # lines meet: fill the centre so it is a solid plus
	if spec["dot"]:
		var dt := maxf(1.0, roundf(float(spec["dot_t"]) * k))
		_bar(Rect2(c.x - floorf(dt / 2.0), c.y - floorf(dt / 2.0), dt, dt), col, float(spec["dot_a"]), k)

func _bar(r: Rect2, col: Color, alpha: float, k: float) -> void:
	if spec["outline"] and float(spec["outline_a"]) > 0.0:
		var ot := maxf(1.0, roundf(float(spec["outline_t"]) * k))
		draw_rect(r.grow(ot), Color(0, 0, 0, float(spec["outline_a"]) * alpha))
	draw_rect(r, Color(col.r, col.g, col.b, alpha))
