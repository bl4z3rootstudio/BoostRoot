extends Node3D
## Poligon: menu, HUD and the drill logic.

enum State { MENU, PLAYING, PAUSED, RESULTS }

var state := State.MENU
var world: RangeWorld
var player: Player
var rng := RandomNumberGenerator.new()

# ---- run state ----
var drill: Dictionary = {}
var bots: Array[Bot] = []
var countdown := 0.0
var time_left := 0.0
var elapsed := 0.0
var next_spawn_ms := 0
var rounds := 0
var kills := 0
var shots := 0
var hits := 0
var heads := 0
var moving_shots := 0
var reactions: Array[float] = []
var placement_errs: Array[float] = []
var stop_times: Array[float] = []
var died := false
var deaths := 0
## Warm-up routine: remaining drill ids, and per-drill results collected so far.
var routine: Array[String] = []
var routine_results: Array[Dictionary] = []
var _free_slots: Array[Vector3] = []

# ---- UI ----
var ui: CanvasLayer
var hud: Control
var xhair: Crosshair
var lbl_top: Label
var lbl_left: Label
var lbl_right: Label
var lbl_center: Label
var lbl_sub: Label
var lbl_fps: Label
var hitmarker: Control
var vignette: ColorRect
var speed_bar: ProgressBar
var speed_lbl: Label
var menu: Control
var pause_panel: Control
var results_panel: Control
var results_lbl: RichTextLabel
var drill_buttons := {}
var selected_drill := "duel"
var drill_desc: Label
var best_lbl: Label
var cm_lbl: Label
var preview_xhair: Crosshair
var sens_edit: LineEdit
var _hit_t := 0.0
var _vig_t := 0.0
var _msg_t := 0.0

const ACCENT := Color(0.98, 0.29, 0.34)
const TEAL := Color(0.34, 0.9, 0.84)

func _ready() -> void:
	rng.randomize()
	world = RangeWorld.new()
	add_child(world)
	world.build()
	player = Player.new()
	add_child(player)
	player.shot.connect(_on_shot)
	_build_ui()
	_show_menu()
	get_window().focus_exited.connect(func() -> void:
		if state == State.PLAYING:
			_pause())
	if Settings.selftest:
		_selftest.call_deferred()
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--screenshot="):
			_screenshot.call_deferred(a.substr(13))

## Dev aid: renders the menu and a drill to PNGs (used to check visuals without a GPU).
func _screenshot(prefix: String) -> void:
	for i in 30:
		await get_tree().process_frame
	get_viewport().get_texture().get_image().save_png(prefix + "_menu.png")
	_start("free")
	countdown = 0.0
	player.can_shoot = true
	for i in 30:
		await get_tree().process_frame
	player.set_look(-6, -1)
	for i in 10:
		await get_tree().process_frame
	get_viewport().get_texture().get_image().save_png(prefix + "_range.png")
	_finish()
	for i in 5:
		await get_tree().process_frame
	get_viewport().get_texture().get_image().save_png(prefix + "_results.png")
	get_tree().quit()

# =====================================================================  input

func _unhandled_input(event: InputEvent) -> void:
	if state == State.PLAYING and event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		# screen_relative = raw device counts in captured mode (not scaled by window stretch) → exact game feel.
		player.look((event as InputEventMouseMotion).screen_relative)
	elif event is InputEventKey and event.pressed and not event.echo:
		var k := (event as InputEventKey).physical_keycode
		if k == KEY_ESCAPE:
			match state:
				State.PLAYING: _pause()
				State.PAUSED: _resume()
				State.RESULTS: _show_menu()
		elif state == State.RESULTS and (k == KEY_SPACE or k == KEY_ENTER):
			_start(selected_drill)
	elif state == State.PAUSED and event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT \
			and not pause_panel.get_global_rect().has_point(event.position):
		_resume()

# =====================================================================  drill flow

func _start(id: String, from_routine := false) -> void:
	if id == "warmup":
		routine = ["flick", "micro", "strafe", "duel"]
		routine_results.clear()
		_start(routine.pop_front(), true)
		selected_drill = "warmup"
		_select_routine_drill()
		return
	if not from_routine:
		routine.clear()
	selected_drill = id
	drill = Drills.get_drill(id).duplicate()
	if from_routine:
		drill["timed"] = true
		drill.erase("rounds")
	_clear_bots()
	kills = 0; shots = 0; hits = 0; heads = 0; moving_shots = 0; rounds = 0
	reactions.clear(); placement_errs.clear(); stop_times.clear()
	died = false
	deaths = 0
	elapsed = 0.0
	time_left = (30.0 if from_routine else float(Settings.data["duration"])) if drill.get("timed", false) else 0.0
	countdown = 3.0
	player.global_position = Vector3.ZERO
	player.velocity = Vector3.ZERO
	player.set_look(0.0, 0.0)
	player.ammo = int(player.weapon()["mag"])
	player.reloading = 0.0
	player.can_shoot = false
	player.can_move = id in ["counter", "free", "strafe", "flick", "micro", "dm"]
	xhair.set_code(String(Settings.data["xhair"]))
	speed_bar.visible = id == "counter"
	speed_lbl.visible = id == "counter"
	next_spawn_ms = 0
	if id == "free":
		_free_slots = [Vector3(-2.5, 0, -8), Vector3(2.5, 0, -8), Vector3(-1.6, 0, -18), Vector3(1.6, 0, -18), Vector3(0, 0, -28)]
		for p in _free_slots:
			_spawn_static(p, 0)
	menu.visible = false
	results_panel.visible = false
	pause_panel.visible = false
	hud.visible = true
	player.cam.get_child(0).visible = true
	state = State.PLAYING
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

func _process(delta: float) -> void:
	lbl_fps.text = "%d FPS" % Engine.get_frames_per_second()
	_hit_t -= delta
	hitmarker.visible = _hit_t > 0.0
	_vig_t -= delta
	vignette.color.a = clampf(_vig_t, 0.0, 0.45)
	if _msg_t > 0.0:
		_msg_t -= delta
		if _msg_t <= 0.0 and countdown <= 0.0:
			lbl_center.text = ""
			lbl_sub.text = ""
	if state != State.PLAYING:
		return
	xhair.move_error = maxf(0.0, player.spread_deg() - float(player.weapon()["first"]) * 0.5 - minf(player.shots_in_burst, 8) * 0.28)
	xhair.fire_error = minf(player.shots_in_burst, 8) * 0.28
	xhair.queue_redraw()
	if countdown > 0.0:
		countdown -= delta
		lbl_center.text = str(ceili(countdown)) if countdown > 0.0 else "BAŞLA"
		lbl_sub.text = String(drill["name"])
		if countdown <= 0.0:
			player.can_shoot = true
			player.can_move = true if _cur() in ["counter", "free", "strafe", "flick", "micro", "dm"] else player.can_move
			_msg(0.5)
			next_spawn_ms = Time.get_ticks_msec() + 300
		_update_hud()
		return
	elapsed += delta
	if drill.get("timed", false):
		time_left -= delta
		if time_left <= 0.0:
			_finish()
			return
	if drill.has("rounds") and rounds >= int(drill["rounds"]) and bots.is_empty():
		_finish()
		return
	_tick_drill()
	_update_hud()

func _tick_drill() -> void:
	var now := Time.get_ticks_msec()
	var alive: Array[Bot] = []
	for b in bots:
		if is_instance_valid(b) and b.alive:
			alive.append(b)
	bots = alive
	# Enemies that were not killed in time shoot back.
	for b in bots:
		if b.deadline_ms > 0 and now - int(b.spawned_at) > b.deadline_ms:
			_enemy_shoots(b)
			return
	match _cur():
		"dm":
			while bots.size() < 2 and now >= next_spawn_ms:
				_spawn_peek(Settings.react_ms(), 1.0)
				next_spawn_ms = now + rng.randi_range(350, 1100)
		"duel":
			if bots.is_empty() and now >= next_spawn_ms:
				_spawn_peek(Settings.react_ms(), 1.0)
		"placement":
			if bots.is_empty() and now >= next_spawn_ms and rounds < int(drill["rounds"]):
				var b := _spawn_peek(int(Settings.react_ms() * 1.3), 1.0)
				rounds += 1
				placement_errs.append(_angle_to(b.head_position_at(b.path_to)))
		"flick":
			if bots.is_empty() and now >= next_spawn_ms:
				_spawn_open(rng.randf_range(-42, 42), rng.randf_range(8, 30), 0)
		"micro":
			while bots.size() < 2 and now >= next_spawn_ms:
				var base := player.yaw
				_spawn_open(base + rng.randf_range(-12, 12), rng.randf_range(5, 10), 0, true)
		"turn":
			if bots.is_empty() and now >= next_spawn_ms and rounds < int(drill["rounds"]):
				rounds += 1
				var side := -1.0 if rng.randf() < 0.5 else 1.0
				var ang := player.yaw + side * rng.randf_range(100, 175)
				var b := _spawn_open(ang, rng.randf_range(6, 14), Settings.react_ms() * 2, false, true)
				var wr: WeakRef = weakref(b)
				for i in 3:
					get_tree().create_timer(i * 0.18).timeout.connect(func() -> void:
						var bb: Object = wr.get_ref()
						if bb != null and (bb as Bot).alive:
							Sfx.play_at("step", self, (bb as Bot).global_position + Vector3(0, 0.1, 0), 4.0))
		"strafe":
			if bots.is_empty() and now >= next_spawn_ms:
				var d := rng.randf_range(12, 22)
				var a := deg_to_rad(player.yaw + rng.randf_range(-20, 20))
				var center := player.global_position + Vector3(-sin(a), 0, -cos(a)) * d
				center.y = 0
				var b := _new_bot(int(Settings.react_ms() * 1.6))
				b.global_position = center
				b.face(player.global_position)
				b.start_strafe(center, 2.4, float(Settings.game()["run"]) * float(Settings.data["bot_speed"]))
		"counter":
			# A target appears only while you are running: you have to stop to hit it.
			if bots.is_empty() and now >= next_spawn_ms and player.speed_frac() > 0.6:
				_spawn_open(player.yaw + rng.randf_range(-25, 25), rng.randf_range(9, 16), 0)
			if player.last_stop_ms >= 0:
				stop_times.append(float(player.last_stop_ms))
				player.last_stop_ms = -1
		"free":
			pass

## Drill id that is actually running (inside the warm-up routine this is the current step).
var _routine_step := ""
func _cur() -> String:
	return _routine_step if selected_drill == "warmup" else selected_drill

func _select_routine_drill() -> void:
	_routine_step = String(drill["id"])
	lbl_sub.text = "Isınma: " + String(drill["name"])

func _new_bot(deadline: int) -> Bot:
	var b := Bot.new()
	add_child(b)
	b.deadline_ms = deadline if (deadline > 0 and _cur() != "free") else 0
	b.spawned_at = Time.get_ticks_msec()
	b.died.connect(_on_bot_died)
	bots.append(b)
	return b

## Peek out from a random cover (duel / placement).
func _spawn_peek(deadline: int, _speed_mult: float) -> Bot:
	var pts: Array[Vector3] = []
	# Real peeks vary: normal swing, wide swing, jiggle, crouch peek.
	var kind := rng.randi() % 5 if _cur() in ["duel", "dm"] else 0
	var step := rng.randf_range(1.6, 2.6) if kind == 1 else rng.randf_range(0.7, 1.4)
	for attempt in 12:
		var c: Dictionary = world.covers[rng.randi() % world.covers.size()]
		pts = world.peek_points(c, player.global_position, rng.randf() < 0.5, step)
		# Only peeks that end fully visible and start fully hidden.
		if _visible(pts[1] + Vector3(0, 1.6, 0)) and not _visible(pts[0] + Vector3(0, 1.6, 0)):
			break
	var b := _new_bot(deadline)
	var speed := float(Settings.game()["run"]) * float(Settings.data["bot_speed"])
	b.peek(pts[0], pts[1], speed)
	b.face(player.global_position)
	if kind == 2:
		b.jiggles = 2
	elif kind == 3:
		b.scale = Vector3(1.0, 0.72, 1.0)   # crouch peek: head lower than you expect
	# The clock starts when the enemy clears the edge of the cover, not while it is still hidden.
	b.spawned_at = Time.get_ticks_msec() + int(0.4 / speed * 1000.0)
	return b

## Stand in the open at a yaw angle (degrees, same convention as the player) and distance.
func _spawn_open(yaw_deg: float, dist: float, deadline: int, small := false, behind := false) -> Bot:
	var p := Vector3.ZERO
	for attempt in 16:
		var a := deg_to_rad(yaw_deg + (0.0 if attempt == 0 else rng.randf_range(-6, 6)))
		var dd := dist if attempt == 0 else dist * rng.randf_range(0.7, 1.0)
		p = player.global_position + Vector3(-sin(a), 0, -cos(a)) * dd
		p.y = 0
		p.x = clampf(p.x, -42, 42)
		p.z = clampf(p.z, -58, 22)
		if _visible(p + Vector3(0, 1.6 * (0.85 if small else 1.0), 0)):
			break
	var b := _new_bot(deadline)
	b.global_position = p
	b.face(player.global_position)
	if small:
		b.scale = Vector3.ONE * 0.85
	if behind:
		b.spawned_at = Time.get_ticks_msec()
	return b

## True when nothing solid is between the player's eye and `p`.
func _visible(p: Vector3) -> bool:
	var q := PhysicsRayQueryParameters3D.create(player.cam.global_position, p, 1)
	q.exclude = [player.get_rid()]
	return get_world_3d().direct_space_state.intersect_ray(q).is_empty()

func _spawn_static(p: Vector3, deadline: int) -> void:
	var b := _new_bot(deadline)
	b.global_position = p
	b.face(Vector3(p.x, 0, 10))

func _on_shot(res: Dictionary) -> void:
	if state != State.PLAYING:
		return
	shots += 1
	_impact(res["point"], res["hit"])
	if res["moving"]:
		moving_shots += 1
	if not res["hit"]:
		return
	var b: Bot = res["bot"]
	var part: String = res["part"]
	hits += 1
	if part == "head":
		heads += 1
	if bool(Settings.data["headshot_only"]) and part != "head" and _cur() != "free":
		Sfx.play("body", -10.0)
		return
	b.hit(part, player.weapon())
	_hit_t = 0.1
	if part == "head":
		Sfx.play("head", -4.0)
	else:
		Sfx.play("body", -6.0)

func _on_bot_died(b: Bot) -> void:
	kills += 1
	var t := float(Time.get_ticks_msec() - int(b.spawned_at))
	if _cur() != "free" and _cur() != "counter":
		reactions.append(t)
	Sfx.play("kill", -8.0)
	var gap := {"duel": rng.randi_range(600, 1800), "placement": rng.randi_range(700, 1600), "turn": rng.randi_range(700, 1500),
		"strafe": 450, "flick": 60, "micro": 40, "counter": 200, "dm": 500}
	next_spawn_ms = Time.get_ticks_msec() + int(gap.get(_cur(), 300))
	if _cur() == "free":
		var pos := b.global_position
		get_tree().create_timer(1.2).timeout.connect(func() -> void:
			if state == State.PLAYING and _cur() == "free":
				_spawn_static(Vector3(pos.x, 0, pos.z), 0))

var _impacts: Array[MeshInstance3D] = []
var _impact_mesh: SphereMesh
var _impact_mat: StandardMaterial3D

## Small marker where each bullet landed, so you can see your spread / misses. Pooled, fades by itself.
func _impact(p: Vector3, on_bot: bool) -> void:
	if _impact_mesh == null:
		_impact_mesh = SphereMesh.new()
		_impact_mesh.radius = 0.025
		_impact_mesh.height = 0.05
		_impact_mesh.radial_segments = 6
		_impact_mesh.rings = 3
		_impact_mat = StandardMaterial3D.new()
		_impact_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		_impact_mat.albedo_color = Color(1.0, 0.85, 0.3)
	if on_bot:
		return
	var m: MeshInstance3D
	if _impacts.size() < 40:
		m = MeshInstance3D.new()
		m.mesh = _impact_mesh
		m.material_override = _impact_mat
		m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(m)
	else:
		m = _impacts.pop_front()
	_impacts.append(m)
	m.global_position = p
	m.visible = true

func _enemy_shoots(b: Bot) -> void:
	Sfx.play("enemy", -2.0)
	_vig_t = 0.6
	if _cur() == "duel" and bool(Settings.data["survival"]) and selected_drill != "warmup":
		died = true
		_finish("ÖLDÜN — %d ms içinde vuramadın" % Settings.react_ms())
		return
	# Not survival: count as a miss and move on.
	deaths += 1
	b.alive = false
	b.queue_free()
	bots.erase(b)
	next_spawn_ms = Time.get_ticks_msec() + 900
	_msg(0.8, "Vuruldun", "")

func _clear_bots() -> void:
	for m in _impacts:
		m.visible = false
	for b in bots:
		if is_instance_valid(b):
			b.queue_free()
	bots.clear()
	for c in get_children():
		if c is Bot:
			c.queue_free()

func _angle_to(p: Vector3) -> float:
	var fwd := -player.cam.global_transform.basis.z
	var to := (p - player.cam.global_position).normalized()
	return rad_to_deg(fwd.angle_to(to))

func _finish(reason := "") -> void:
	state = State.RESULTS
	player.can_shoot = false
	player.can_move = false
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	var acc := float(hits) / shots if shots > 0 else 0.0
	var hs := float(heads) / hits if hits > 0 else 0.0
	var avg_r := _avg(reactions)
	var speed_bonus := 0.0
	for r in reactions:
		speed_bonus += maxf(0.0, 1000.0 - r) / 10.0
	var score := roundf(kills * 100.0 * (0.5 + 0.5 * acc) + speed_bonus)
	if _cur() == "placement" and not placement_errs.is_empty():
		score = roundf(score * clampf(1.5 - _avg(placement_errs) / 20.0, 0.5, 1.5))
	if deaths > 0:
		score = maxf(0.0, score - deaths * 100.0)
	var r := {
		"drill": _cur(), "deaths": deaths, "game": Settings.data["game"], "sens": Settings.sens(), "score": score,
		"kills": kills, "shots": shots, "hits": hits, "heads": heads, "acc": snappedf(acc, 0.001), "hs": snappedf(hs, 0.001),
		"avg_react": roundf(avg_r), "died": died, "ts": int(Time.get_unix_time_from_system()), "stretched": Settings.data["stretched"],
		"placement": snappedf(_avg(placement_errs), 0.01), "stop_ms": roundf(_avg(stop_times)), "moving_shots": moving_shots,
		"seconds": roundf(elapsed),
	}
	_clear_bots()
	if selected_drill == "warmup":
		routine_results.append(r)
		if not routine.is_empty():
			# Next step of the warm-up straight away.
			state = State.PLAYING
			_start(routine.pop_front(), true)
			selected_drill = "warmup"
			_select_routine_drill()
			return
		# Whole routine done: one combined result.
		r = {"drill": "warmup", "game": Settings.data["game"], "sens": Settings.sens(), "score": 0.0, "kills": 0, "shots": 0, "hits": 0, "heads": 0,
			"ts": int(Time.get_unix_time_from_system()), "died": false, "deaths": 0}
		var reacts: Array[float] = []
		for x in routine_results:
			for k in ["score", "kills", "shots", "hits", "heads", "deaths"]:
				r[k] = float(r[k]) + float(x.get(k, 0))
			if float(x.get("avg_react", 0)) > 0:
				reacts.append(float(x["avg_react"]))
		r["acc"] = snappedf(r["hits"] / r["shots"], 0.001) if r["shots"] > 0 else 0.0
		r["hs"] = snappedf(r["heads"] / r["hits"], 0.001) if r["hits"] > 0 else 0.0
		r["avg_react"] = roundf(_avg(reacts))
		score = r["score"]; kills = int(r["kills"]); shots = int(r["shots"]); hits = int(r["hits"]); heads = int(r["heads"])
		acc = r["acc"]; hs = r["hs"]; avg_r = r["avg_react"]
		drill = Drills.get_drill("warmup")
		reactions.clear()
		if avg_r > 0:
			reactions.append(avg_r)
	var prev := Settings.best(selected_drill)
	var is_best := Settings.add_result(r) if (kills > 0 or selected_drill != "free") else false
	var t := "[center][font_size=46][b]%s[/b][/font_size]\n" % String(drill["name"])
	if reason != "":
		t += "[color=#ff5c63][font_size=26]%s[/font_size][/color]\n" % reason
	t += "\n[font_size=64][color=#%s]%d[/color][/font_size]\n" % [TEAL.to_html(false), int(score)]
	t += ("[color=#ffd166]YENİ REKOR![/color]\n" if is_best and prev >= 0 else ("En iyi: %d\n" % int(prev) if prev >= 0 else "İlk skorun kaydedildi\n"))
	t += "\n[font_size=24]Öldürme: [b]%d[/b]    İsabet: [b]%%%d[/b]    Headshot: [b]%%%d[/b][/font_size]\n" % [kills, roundi(acc * 100), roundi(hs * 100)]
	if not reactions.is_empty():
		t += "[font_size=24]Ortalama tepki / öldürme süresi: [b]%d ms[/b]    En hızlı: [b]%d ms[/b][/font_size]\n" % [roundi(avg_r), roundi(reactions.min())]
	if not placement_errs.is_empty():
		t += "[font_size=24]Nişan kafaya ortalama [b]%.1f°[/b] uzaktı  (%s)[/font_size]\n" % [_avg(placement_errs), "çok iyi" if _avg(placement_errs) < 4 else ("iyi" if _avg(placement_errs) < 9 else "nişanı kafa hizasında tut")]
	if not stop_times.is_empty():
		t += "[font_size=24]Ortalama durma süresi: [b]%d ms[/b]  (Riot ölçümü: karşı-strafe ile ~104 ms)[/font_size]\n" % roundi(_avg(stop_times))
	if selected_drill == "warmup":
		for x in routine_results:
			t += "[font_size=20]%s: [b]%d[/b] puan · %d öldürme · %%%d isabet[/font_size]\n" % [Drills.get_drill(String(x["drill"]))["name"], int(x["score"]), int(x["kills"]), roundi(float(x["acc"]) * 100)]
	if deaths > 0 and selected_drill == "dm":
		t += "[font_size=22][color=#ff5c63]%d kez vuruldun (-%d puan)[/color][/font_size]\n" % [deaths, deaths * 100]
	if moving_shots > 0:
		t += "[font_size=22][color=#ffb347]Hareket ederken %d mermi attın — bunlar isabetsiz gider.[/color][/font_size]\n" % moving_shots
	t += "\n[font_size=20][color=#9aa0b4]Boşluk: tekrar   ·   Esc: menü[/color][/font_size][/center]"
	results_lbl.text = t
	player.cam.get_child(0).visible = false
	hud.visible = false
	results_panel.visible = true

func _avg(a: Array[float]) -> float:
	if a.is_empty():
		return 0.0
	var s := 0.0
	for x in a:
		s += x
	return s / a.size()

func _pause() -> void:
	state = State.PAUSED
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	pause_panel.visible = true
	get_tree().paused = false
	player.can_shoot = false

func _resume() -> void:
	pause_panel.visible = false
	state = State.PLAYING
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	player.can_shoot = countdown <= 0.0
	# Enemies' timers must not run while paused.
	for b in bots:
		if is_instance_valid(b):
			b.spawned_at = Time.get_ticks_msec()

func _show_menu() -> void:
	state = State.MENU
	_clear_bots()
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	hud.visible = false
	results_panel.visible = false
	pause_panel.visible = false
	menu.visible = true
	player.can_move = false
	player.can_shoot = false
	player.global_position = Vector3.ZERO
	player.set_look(0, 0)
	player.cam.get_child(0).visible = false
	_select_drill(selected_drill)

func _msg(seconds: float, big := "", small := "") -> void:
	if big != "" or small != "":
		lbl_center.text = big
		lbl_sub.text = small
	_msg_t = seconds

func _update_hud() -> void:
	var acc := float(hits) / shots * 100.0 if shots > 0 else 0.0
	if drill.get("timed", false):
		lbl_top.text = "%d" % ceili(maxf(time_left, 0.0))
	elif drill.has("rounds"):
		lbl_top.text = "%d / %d" % [rounds, int(drill["rounds"])]
	else:
		lbl_top.text = "SERİ %d" % kills if _cur() == "duel" else "%d" % kills
	lbl_left.text = "Öldürme %d\nİsabet %%%d\nHS %%%d" % [kills, roundi(acc), roundi(float(heads) / hits * 100.0) if hits > 0 else 0]
	var r := ""
	if not reactions.is_empty():
		r = "Tepki %d ms\n" % roundi(_avg(reactions))
	r += "%d / %d" % [player.ammo, int(player.weapon()["mag"])]
	if player.reloading > 0.0:
		r += "  ŞARJÖR"
	lbl_right.text = r
	if speed_bar.visible:
		var f := player.speed_frac()
		speed_bar.value = f * 100.0
		var ok := f <= float(Settings.game()["deadzone"])
		speed_bar.modulate = Color(0.4, 1.0, 0.6) if ok else Color(1.0, 0.45, 0.4)
		speed_lbl.text = ("İSABETLİ" if ok else "HAREKETLİ — dur!") + ("    son durma: %d ms" % roundi(stop_times[-1]) if not stop_times.is_empty() else "")

# =====================================================================  UI

func _theme() -> Theme:
	var th := Theme.new()
	th.default_font_size = 18
	var panel := StyleBoxFlat.new()
	panel.bg_color = Color(0.07, 0.07, 0.11, 0.94)
	panel.set_corner_radius_all(14)
	panel.set_content_margin_all(18)
	panel.border_color = Color(1, 1, 1, 0.06)
	panel.set_border_width_all(1)
	th.set_stylebox("panel", "PanelContainer", panel)
	var btn := StyleBoxFlat.new()
	btn.bg_color = Color(0.13, 0.13, 0.19)
	btn.set_corner_radius_all(10)
	btn.set_content_margin_all(12)
	var btn_h := btn.duplicate() as StyleBoxFlat
	btn_h.bg_color = Color(0.19, 0.19, 0.27)
	var btn_p := btn.duplicate() as StyleBoxFlat
	btn_p.bg_color = ACCENT.darkened(0.15)
	th.set_stylebox("normal", "Button", btn)
	th.set_stylebox("hover", "Button", btn_h)
	th.set_stylebox("pressed", "Button", btn_p)
	th.set_stylebox("focus", "Button", StyleBoxEmpty.new())
	var dis := btn.duplicate() as StyleBoxFlat
	dis.bg_color = Color(0.1, 0.1, 0.13)
	th.set_stylebox("disabled", "Button", dis)
	var le := StyleBoxFlat.new()
	le.bg_color = Color(0.04, 0.04, 0.07)
	le.set_corner_radius_all(8)
	le.set_content_margin_all(8)
	le.border_color = Color(1, 1, 1, 0.12)
	le.set_border_width_all(1)
	th.set_stylebox("normal", "LineEdit", le)
	th.set_stylebox("normal", "OptionButton", btn)
	th.set_stylebox("hover", "OptionButton", btn_h)
	th.set_color("font_color", "Label", Color(0.92, 0.92, 0.96))
	return th

func _label(text: String, size := 18, color := Color(0.92, 0.92, 0.96)) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	return l

func _build_ui() -> void:
	ui = CanvasLayer.new()
	add_child(ui)
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.theme = _theme()
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ui.add_child(root)
	# ---------------- HUD
	hud = Control.new()
	hud.set_anchors_preset(Control.PRESET_FULL_RECT)
	hud.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(hud)
	vignette = ColorRect.new()
	vignette.set_anchors_preset(Control.PRESET_FULL_RECT)
	vignette.color = Color(0.8, 0.05, 0.08, 0.0)
	vignette.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(vignette)
	xhair = Crosshair.new()
	hud.add_child(xhair)
	hitmarker = _make_hitmarker()
	hud.add_child(hitmarker)
	lbl_top = _label("", 34)
	lbl_top.set_anchors_preset(Control.PRESET_CENTER_TOP)
	lbl_top.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	lbl_top.position = Vector2(-150, 18)
	lbl_top.size = Vector2(300, 50)
	lbl_top.add_theme_constant_override("outline_size", 6)
	lbl_top.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.6))
	hud.add_child(lbl_top)
	lbl_left = _label("", 18)
	lbl_left.position = Vector2(28, 70)
	lbl_left.add_theme_constant_override("outline_size", 5)
	lbl_left.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.6))
	hud.add_child(lbl_left)
	lbl_right = _label("", 18)
	lbl_right.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	lbl_right.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	lbl_right.position = Vector2(-330, -110)
	lbl_right.size = Vector2(300, 80)
	lbl_right.add_theme_constant_override("outline_size", 5)
	lbl_right.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.6))
	hud.add_child(lbl_right)
	lbl_center = _label("", 92, Color.WHITE)
	lbl_center.set_anchors_preset(Control.PRESET_CENTER)
	lbl_center.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	lbl_center.position = Vector2(-400, -230)
	lbl_center.size = Vector2(800, 120)
	lbl_center.add_theme_constant_override("outline_size", 10)
	lbl_center.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.55))
	hud.add_child(lbl_center)
	lbl_sub = _label("", 26, TEAL)
	lbl_sub.set_anchors_preset(Control.PRESET_CENTER)
	lbl_sub.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	lbl_sub.position = Vector2(-400, -110)
	lbl_sub.size = Vector2(800, 40)
	hud.add_child(lbl_sub)
	lbl_fps = _label("", 14, Color(1, 1, 1, 0.55))
	lbl_fps.position = Vector2(28, 24)
	root.add_child(lbl_fps)
	speed_bar = ProgressBar.new()
	speed_bar.show_percentage = false
	speed_bar.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	speed_bar.position = Vector2(-200, -150)
	speed_bar.size = Vector2(400, 14)
	hud.add_child(speed_bar)
	speed_lbl = _label("", 18)
	speed_lbl.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	speed_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	speed_lbl.position = Vector2(-300, -185)
	speed_lbl.size = Vector2(600, 30)
	hud.add_child(speed_lbl)
	# ---------------- Menu, pause, results
	menu = _build_menu()
	root.add_child(menu)
	pause_panel = _build_pause()
	root.add_child(pause_panel)
	results_panel = PanelContainer.new()
	results_panel.set_anchors_preset(Control.PRESET_CENTER)
	results_panel.custom_minimum_size = Vector2(900, 560)
	results_panel.position = Vector2(-450, -280)
	results_lbl = RichTextLabel.new()
	results_lbl.bbcode_enabled = true
	results_lbl.fit_content = true
	results_lbl.scroll_active = false
	results_lbl.custom_minimum_size = Vector2(860, 520)
	var rv := VBoxContainer.new()
	rv.add_child(results_lbl)
	var rb := HBoxContainer.new()
	rb.alignment = BoxContainer.ALIGNMENT_CENTER
	rb.add_theme_constant_override("separation", 16)
	rb.add_child(_button("Tekrar", func() -> void: _start(selected_drill), true))
	rb.add_child(_button("Menü", _show_menu))
	rv.add_child(rb)
	results_panel.add_child(rv)
	root.add_child(results_panel)

func _make_hitmarker() -> Control:
	var c := Control.new()
	c.set_anchors_preset(Control.PRESET_FULL_RECT)
	c.mouse_filter = Control.MOUSE_FILTER_IGNORE
	c.draw.connect(func() -> void:
		var m := c.get_viewport_rect().size * 0.5
		var k := c.get_viewport_rect().size.y / 1080.0
		for d in [Vector2(1, 1), Vector2(-1, 1), Vector2(1, -1), Vector2(-1, -1)]:
			c.draw_line(m + d * 9.0 * k, m + d * 17.0 * k, Color(1, 1, 1, 0.9), 2.0 * k))
	return c

func _button(text: String, cb: Callable, primary := false) -> Button:
	var b := Button.new()
	b.text = text
	b.custom_minimum_size = Vector2(160, 48)
	if primary:
		var s := StyleBoxFlat.new()
		s.bg_color = ACCENT
		s.set_corner_radius_all(10)
		s.set_content_margin_all(12)
		b.add_theme_stylebox_override("normal", s)
		var h := s.duplicate() as StyleBoxFlat
		h.bg_color = ACCENT.lightened(0.12)
		b.add_theme_stylebox_override("hover", h)
		b.add_theme_font_size_override("font_size", 20)
	b.pressed.connect(cb)
	return b

func _build_menu() -> Control:
	var bg := ColorRect.new()
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.color = Color(0.03, 0.03, 0.06, 0.72)
	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 48)
	bg.add_child(margin)
	var cols := HBoxContainer.new()
	cols.add_theme_constant_override("separation", 28)
	margin.add_child(cols)
	# Left: title, games, drills
	var left := VBoxContainer.new()
	left.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	left.add_theme_constant_override("separation", 16)
	cols.add_child(left)
	var title := _label("POLİGON", 54, Color.WHITE)
	left.add_child(title)
	left.add_child(_label("BoostRoot · oyunla birebir aynı hassasiyetle nişan çalışması", 18, Color(0.62, 0.64, 0.74)))
	var games := HBoxContainer.new()
	games.add_theme_constant_override("separation", 10)
	for id in GameDefs.GAMES.keys():
		var g: Dictionary = GameDefs.GAMES[id]
		var gb := Button.new()
		gb.text = String(g["name"]) + ("" if g.get("enabled", false) else "  · yakında")
		gb.disabled = not g.get("enabled", false)
		gb.toggle_mode = true
		gb.button_pressed = id == Settings.data["game"]
		gb.custom_minimum_size = Vector2(0, 44)
		games.add_child(gb)
	left.add_child(games)
	var grid := GridContainer.new()
	grid.columns = 4
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 12)
	for d in Drills.LIST:
		var b := Button.new()
		b.text = "%s\n%s" % [d["icon"], d["name"]]
		b.custom_minimum_size = Vector2(190, 110)
		b.toggle_mode = true
		b.add_theme_font_size_override("font_size", 19)
		var id: String = d["id"]
		b.pressed.connect(func() -> void: _select_drill(id))
		drill_buttons[id] = b
		grid.add_child(b)
	left.add_child(grid)
	var info := PanelContainer.new()
	var iv := VBoxContainer.new()
	iv.add_theme_constant_override("separation", 8)
	drill_desc = _label("", 18)
	drill_desc.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	drill_desc.custom_minimum_size = Vector2(600, 0)
	best_lbl = _label("", 18, TEAL)
	iv.add_child(drill_desc)
	iv.add_child(best_lbl)
	var start := _button("BAŞLA", func() -> void: _start(selected_drill), true)
	start.custom_minimum_size = Vector2(260, 58)
	iv.add_child(start)
	info.add_child(iv)
	left.add_child(info)
	left.add_child(_label("WASD hareket · Shift yürü · Ctrl eğil · Boşluk zıpla · Sol tık ateş · R şarjör · Esc duraklat", 15, Color(0.55, 0.57, 0.66)))
	# Right: settings
	var right := PanelContainer.new()
	right.custom_minimum_size = Vector2(470, 0)
	var scroll := ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	right.add_child(scroll)
	var sv := VBoxContainer.new()
	sv.add_theme_constant_override("separation", 10)
	sv.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(sv)
	cols.add_child(right)
	sv.add_child(_label("AYARLAR", 24, Color.WHITE))
	# Sensitivity — the most important setting
	sv.add_child(_label("Oyundaki hassasiyetin (%s)" % String(Settings.game()["name"]), 16, Color(0.7, 0.72, 0.8)))
	sens_edit = LineEdit.new()
	sens_edit.text = str(Settings.sens())
	sens_edit.text_changed.connect(func(t: String) -> void:
		var v := t.replace(",", ".")
		if v.is_valid_float() and v.to_float() > 0.0:
			Settings.set_sens(v.to_float())
			Settings.save()
			_update_cm())
	sv.add_child(sens_edit)
	var dpi_row := HBoxContainer.new()
	dpi_row.add_child(_label("Fare DPI  ", 16, Color(0.7, 0.72, 0.8)))
	var dpi := LineEdit.new()
	dpi.text = str(int(Settings.data["dpi"]))
	dpi.custom_minimum_size = Vector2(100, 0)
	dpi.text_changed.connect(func(t: String) -> void:
		if t.is_valid_int() and t.to_int() > 0:
			Settings.data["dpi"] = t.to_int()
			Settings.save()
			_update_cm())
	dpi_row.add_child(dpi)
	cm_lbl = _label("", 16, TEAL)
	dpi_row.add_child(cm_lbl)
	sv.add_child(dpi_row)
	sv.add_child(_label("Dönüş oyundaki formülle hesaplanır: hassasiyet × %s° / fare adımı.\nGörüş açısı: %s° yatay (oyunla aynı)." % [str(Settings.game()["yaw"]), str(Settings.game()["hfov"])], 13, Color(0.55, 0.57, 0.66)))
	# Stretched resolution
	var st := CheckBox.new()
	st.text = "Gerilmiş çözünürlük (4:3 stretched)"
	st.button_pressed = bool(Settings.data["stretched"])
	sv.add_child(st)
	var res := OptionButton.new()
	var res_list := [[1440, 1080], [1280, 960], [1280, 1024], [1080, 1080], [1024, 768]]
	for r in res_list:
		res.add_item("%d × %d" % [r[0], r[1]])
	for i in res_list.size():
		if res_list[i][0] == int(Settings.data["stretch_w"]) and res_list[i][1] == int(Settings.data["stretch_h"]):
			res.select(i)
	res.disabled = not st.button_pressed
	st.toggled.connect(func(on: bool) -> void:
		Settings.data["stretched"] = on
		res.disabled = not on
		Settings.apply_display()
		Settings.save())
	res.item_selected.connect(func(i: int) -> void:
		Settings.data["stretch_w"] = res_list[i][0]
		Settings.data["stretch_h"] = res_list[i][1]
		Settings.apply_display()
		Settings.save())
	sv.add_child(res)
	# Crosshair code
	sv.add_child(_label("VALORANT nişangah kodu", 16, Color(0.7, 0.72, 0.8)))
	sv.add_child(_label("Oyunda: Ayarlar → Nişangâh → Profil kodunu dışa aktar → buraya yapıştır.", 13, Color(0.55, 0.57, 0.66)))
	var xh := LineEdit.new()
	xh.text = String(Settings.data["xhair"])
	sv.add_child(xh)
	var pv := PanelContainer.new()
	pv.custom_minimum_size = Vector2(0, 110)
	var pvs := StyleBoxFlat.new()
	pvs.bg_color = Color(0.36, 0.42, 0.46)
	pvs.set_corner_radius_all(10)
	pv.add_theme_stylebox_override("panel", pvs)
	preview_xhair = Crosshair.new()
	pv.add_child(preview_xhair)
	preview_xhair.set_code(String(Settings.data["xhair"]))
	var xh_status := _label("", 14, TEAL)
	xh.text_changed.connect(func(t: String) -> void:
		if Crosshair.parse(t) != null:
			Settings.data["xhair"] = t.strip_edges()
			Settings.save()
			preview_xhair.set_code(t)
			xh_status.text = "✓ Nişangah alındı"
			xh_status.add_theme_color_override("font_color", TEAL)
		else:
			xh_status.text = "Geçersiz kod (\"0;P;\" ile başlamalı)"
			xh_status.add_theme_color_override("font_color", ACCENT))
	sv.add_child(pv)
	sv.add_child(xh_status)
	# Difficulty
	sv.add_child(_label("Zorluk (düşmanın seni vurma süresi)", 16, Color(0.7, 0.72, 0.8)))
	var diff := OptionButton.new()
	var diffs := [["easy", "Kolay · 1500 ms"], ["normal", "Normal · 1000 ms"], ["hard", "Zor · 700 ms"], ["pro", "Pro · 500 ms"], ["custom", "Özel"]]
	for i in diffs.size():
		diff.add_item(diffs[i][1])
		if diffs[i][0] == Settings.data["difficulty"]:
			diff.select(i)
	sv.add_child(diff)
	var react := SpinBox.new()
	react.min_value = 200
	react.max_value = 4000
	react.step = 50
	react.suffix = "ms"
	react.value = Settings.react_ms()
	react.value_changed.connect(func(v: float) -> void:
		Settings.data["react_ms"] = int(v)
		Settings.save())
	diff.item_selected.connect(func(i: int) -> void:
		Settings.data["difficulty"] = diffs[i][0]
		if Settings.DIFFICULTY.has(diffs[i][0]):
			react.value = Settings.DIFFICULTY[diffs[i][0]]
		Settings.save())
	sv.add_child(react)
	sv.add_child(_slider_row("Düşman hızı", 0.5, 1.5, 0.05, float(Settings.data["bot_speed"]), func(v: float) -> void:
		Settings.data["bot_speed"] = v
		Settings.save(), "x"))
	sv.add_child(_slider_row("Süre", 30, 120, 15, float(Settings.data["duration"]), func(v: float) -> void:
		Settings.data["duration"] = int(v)
		Settings.save(), " sn"))
	var hso := CheckBox.new()
	hso.text = "Sadece headshot sayılsın"
	hso.button_pressed = bool(Settings.data["headshot_only"])
	hso.toggled.connect(func(on: bool) -> void:
		Settings.data["headshot_only"] = on
		Settings.save())
	sv.add_child(hso)
	var surv := CheckBox.new()
	surv.text = "Tepki Düellosu: vurulunca oyun bitsin"
	surv.button_pressed = bool(Settings.data["survival"])
	surv.toggled.connect(func(on: bool) -> void:
		Settings.data["survival"] = on
		Settings.save())
	sv.add_child(surv)
	sv.add_child(_slider_row("Ses", 0, 1, 0.05, float(Settings.data["volume"]), func(v: float) -> void:
		Settings.data["volume"] = v
		Settings.apply_display()
		Settings.save(), ""))
	var fps := OptionButton.new()
	var caps := [0, 144, 165, 240, 360]
	for c in caps:
		fps.add_item("FPS sınırı: yok" if c == 0 else "FPS sınırı: %d" % c)
	fps.select(maxi(0, caps.find(int(Settings.data["fps_cap"]))))
	fps.item_selected.connect(func(i: int) -> void:
		Settings.data["fps_cap"] = caps[i]
		Settings.apply_display()
		Settings.save())
	sv.add_child(fps)
	var quit := _button("Çıkış", func() -> void: get_tree().quit())
	sv.add_child(quit)
	_update_cm()
	return bg

func _slider_row(text: String, lo: float, hi: float, step: float, value: float, cb: Callable, suffix: String) -> Control:
	var row := HBoxContainer.new()
	var l := _label(text, 16, Color(0.7, 0.72, 0.8))
	l.custom_minimum_size = Vector2(130, 0)
	row.add_child(l)
	var s := HSlider.new()
	s.min_value = lo
	s.max_value = hi
	s.step = step
	s.value = value
	s.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var v := _label(("%.2f" % value if step < 1 else "%d" % int(value)) + suffix, 16)
	v.custom_minimum_size = Vector2(70, 0)
	s.value_changed.connect(func(x: float) -> void:
		v.text = ("%.2f" % x if step < 1 else "%d" % int(x)) + suffix
		cb.call(x))
	row.add_child(s)
	row.add_child(v)
	return row

func _update_cm() -> void:
	if cm_lbl:
		var cm := GameDefs.cm_per_360(String(Settings.data["game"]), Settings.sens(), int(Settings.data["dpi"]))
		cm_lbl.text = "  = %.1f cm / 360°  ·  eDPI %d" % [cm, roundi(Settings.sens() * int(Settings.data["dpi"]))] if cm > 0 else ""

func _select_drill(id: String) -> void:
	selected_drill = id
	for k in drill_buttons.keys():
		(drill_buttons[k] as Button).button_pressed = k == id
	var d := Drills.get_drill(id)
	drill_desc.text = String(d["desc"])
	var b := Settings.best(id)
	best_lbl.text = "En iyi skorun: %d" % int(b) if b >= 0 else "Henüz skor yok"

func _build_pause() -> Control:
	var p := PanelContainer.new()
	p.set_anchors_preset(Control.PRESET_CENTER)
	p.custom_minimum_size = Vector2(420, 0)
	p.position = Vector2(-210, -160)
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 12)
	v.add_child(_label("DURAKLATILDI", 30, Color.WHITE))
	v.add_child(_button("Devam (Esc)", _resume, true))
	v.add_child(_button("Yeniden başlat", func() -> void: _start(selected_drill)))
	v.add_child(_button("Bitir ve sonuç", func() -> void: _finish()))
	v.add_child(_button("Menü", _show_menu))
	p.add_child(v)
	return p

# =====================================================================  self test (headless, CI)

func _selftest() -> void:
	var fails: Array[String] = []
	# 1) Sensitivity must match the game exactly: 100 counts × 0.4 × 0.07 = 2.8°.
	Settings.data["game"] = "valorant"
	Settings.set_sens(0.4)
	player.set_look(0, 0)
	player.look(Vector2(100, 0))
	if absf(player.yaw - (-2.8)) > 0.0001:
		fails.append("sens yaw %f" % player.yaw)
	player.look(Vector2(0, -50))
	if absf(player.pitch - 1.4) > 0.0001:
		fails.append("sens pitch %f" % player.pitch)
	if absf(player.cam.fov - 103.0) > 0.001 or player.cam.keep_aspect != Camera3D.KEEP_WIDTH:
		fails.append("fov")
	# 2) Crosshair code parsing
	var x = Crosshair.parse("0;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0")
	if x == null or (x["color"] as Color).to_html(false) != "00ffff" or x["outline"] or int(x["inner"]["l"]) != 4 or x["outer"]["show"]:
		fails.append("crosshair parse")
	if Crosshair.parse("hello") != null:
		fails.append("crosshair reject")
	# 3) Every drill runs and an aimbot can kill in it.
	Settings.data["duration"] = 30
	Settings.data["survival"] = false
	for d in Drills.LIST:
		var id: String = d["id"]
		_start(id)
		countdown = 0.01
		var frames := 0
		while frames < 2400 and state == State.PLAYING and kills < 3:
			await get_tree().physics_frame
			frames += 1
			if id == "counter":
				player.velocity = -player.transform.basis.x * 5.4 if (frames / 60) % 2 == 0 else Vector3.ZERO
			for b in bots:
				if is_instance_valid(b) and b.alive and (b.move_t >= 1.0 or b.strafe):
					var to := b.head_position() - player.cam.global_position
					var yaw_deg := rad_to_deg(atan2(-to.x, -to.z))
					var pitch_deg := rad_to_deg(atan2(to.y, Vector2(to.x, to.z).length()))
					player.set_look(yaw_deg, pitch_deg)
					player.velocity = Vector3.ZERO
					player.fire_cooldown = 0.0
					player.shots_in_burst = 0
					player.fire()
					break
		if kills < 1:
			fails.append("drill %s: no kill (bots=%d frames=%d)" % [id, bots.size(), frames])
		if state == State.PLAYING:
			_finish()
		print("drill ", id, " kills=", kills, " shots=", shots, " heads=", heads)
	# 3b) Warm-up routine walks through all four steps and ends with one combined result.
	_start("warmup")
	var steps := [_cur()]
	for i in 3:
		_finish()
		steps.append(_cur())
	if state != State.PLAYING or steps != ["flick", "micro", "strafe", "duel"]:
		fails.append("warmup steps %s" % str(steps))
	_finish()
	if state != State.RESULTS or routine_results.size() != 4:
		fails.append("warmup end state=%d results=%d" % [state, routine_results.size()])
	# 4) Movement accuracy: still = accurate first shots, running = mostly misses (like the game).
	_start("free")
	countdown = 0.01
	for i in 10:
		await get_tree().physics_frame
	var rates := []
	for run in [false, true]:
		var h := 0
		for i in 40:
			var target: Bot = null
			for b in bots:
				if is_instance_valid(b) and b.alive and absf(b.global_position.z + 18.0) < 0.5:
					target = b
					break
			if target == null:
				await get_tree().physics_frame
				continue
			var to := target.head_position() - player.cam.global_position
			player.set_look(rad_to_deg(atan2(-to.x, -to.z)), rad_to_deg(atan2(to.y, Vector2(to.x, to.z).length())))
			player.velocity = player.transform.basis.x * 5.4 if run else Vector3.ZERO
			player.fire_cooldown = 0.0
			player.shots_in_burst = 0
			var before := heads
			target.hp = 1000000
			player.ammo = 25
			player.reloading = 0.0
			player.fire()
			if heads > before:
				h += 1
			await get_tree().physics_frame
		rates.append(h / 40.0)
	print("accuracy still=", rates[0], " running=", rates[1])
	if rates[0] < 0.85 or rates[1] > 0.5:
		fails.append("movement accuracy %s" % str(rates))
	_finish()
	# 5) Duel: doing nothing gets you shot (survival).
	Settings.data["survival"] = true
	Settings.data["react_ms"] = 300
	_start("duel")
	countdown = 0.01
	var n := 0
	while state == State.PLAYING and n < 900:
		await get_tree().physics_frame
		n += 1
	if not died:
		fails.append("duel survival: player never died")
	if fails.is_empty():
		print("SELFTEST OK")
		get_tree().quit(0)
	else:
		for f in fails:
			printerr("FAIL: ", f)
		get_tree().quit(1)
