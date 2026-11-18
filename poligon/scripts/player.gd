class_name Player
extends CharacterBody3D
## First-person player: raw-mouse look with the game's exact degrees-per-count, the game's FOV,
## VALORANT-like acceleration / stopping, and a Vandal with first-shot accuracy, movement error and recoil.

signal shot(result: Dictionary)

var cam: Camera3D
var yaw := 0.0
var pitch := 0.0
var can_move := true
var can_shoot := true
var crouching := false
var walking := false
var ammo := 25
var reloading := 0.0
var shots_in_burst := 0
var since_shot := 99.0
var fire_cooldown := 0.0
var stop_started_ms := -1     # when the player let go of / countered movement (for stop-timing feedback)
var last_stop_ms := -1
var _was_moving := false
var _viewmodel: Node3D
var _kick := 0.0
var _rng := RandomNumberGenerator.new()
var _bob := 0.0

func _ready() -> void:
	_rng.randomize()
	var col := CollisionShape3D.new()
	var cap := CapsuleShape3D.new()
	cap.radius = 0.3
	cap.height = 1.8
	col.shape = cap
	col.position.y = 0.9
	add_child(col)
	collision_layer = 1
	collision_mask = 1
	cam = Camera3D.new()
	cam.keep_aspect = Camera3D.KEEP_WIDTH      # horizontal FOV stays fixed whatever the aspect ratio (like VALORANT)
	cam.fov = float(Settings.game().get("hfov", 103.0))
	cam.near = 0.03
	cam.far = 400.0
	cam.position.y = float(Settings.game().get("eye", 1.6))
	add_child(cam)
	cam.current = true
	_build_viewmodel()
	ammo = int(weapon()["mag"])

func weapon() -> Dictionary:
	return Settings.game()["weapon"]

func _build_viewmodel() -> void:
	_viewmodel = Node3D.new()
	cam.add_child(_viewmodel)
	_viewmodel.position = Vector3(0.2, -0.2, -0.42)
	_viewmodel.scale = Vector3.ONE * 0.6
	var dark := StandardMaterial3D.new()
	dark.albedo_color = Color(0.3, 0.31, 0.35)
	dark.metallic = 0.6
	dark.roughness = 0.35
	var acc := StandardMaterial3D.new()
	acc.albedo_color = Color(0.85, 0.32, 0.25)
	acc.roughness = 0.5
	var parts := [
		[Vector3(0.05, 0.07, 0.42), Vector3(0, 0, 0), dark],        # receiver
		[Vector3(0.025, 0.025, 0.3), Vector3(0, 0.01, -0.33), dark], # barrel
		[Vector3(0.045, 0.12, 0.05), Vector3(0, -0.08, 0.04), acc],  # magazine
		[Vector3(0.04, 0.09, 0.04), Vector3(0, -0.07, 0.15), dark],  # grip
		[Vector3(0.05, 0.06, 0.16), Vector3(0, -0.01, 0.27), acc],   # stock
		[Vector3(0.02, 0.03, 0.08), Vector3(0, 0.05, -0.05), dark],  # sight
	]
	for p in parts:
		var mi := MeshInstance3D.new()
		var b := BoxMesh.new()
		b.size = p[0]
		mi.mesh = b
		mi.position = p[1]
		mi.material_override = p[2]
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_viewmodel.add_child(mi)

func set_look(y: float, p: float) -> void:
	yaw = y
	pitch = clampf(p, -89.0, 89.0)
	_apply_look()

func _apply_look() -> void:
	rotation_degrees.y = yaw
	cam.rotation_degrees.x = pitch

## Raw mouse counts in. Same formula as the game: angle = counts × sensitivity × yaw.
func look(counts: Vector2) -> void:
	var k := Settings.deg_per_count()
	yaw -= counts.x * k
	pitch = clampf(pitch - counts.y * k, -89.0, 89.0)
	_apply_look()

func max_speed() -> float:
	var g := Settings.game()
	var s := float(g.get("run", 5.4))
	if crouching:
		s *= float(g.get("crouch_mult", 0.5))
	elif walking:
		s *= float(g.get("walk_mult", 0.6))
	return s

func speed_frac() -> float:
	var hv := Vector2(velocity.x, velocity.z).length()
	return hv / float(Settings.game().get("run", 5.4))

## Current random spread in degrees (what the next bullet can deviate by).
func spread_deg() -> float:
	var w := weapon()
	var s := float(w["first"]) * 0.5
	var f := speed_frac()
	var dz := float(Settings.game().get("deadzone", 0.25))
	if not is_on_floor():
		s += 8.0
	elif f > dz:
		var walk_f := float(Settings.game().get("walk_mult", 0.6))
		if f <= walk_f + 0.02:
			s += float(w["walk_err"]) * (f - dz) / maxf(0.01, walk_f - dz)
		else:
			s += lerpf(float(w["walk_err"]), float(w["run_err"]), clampf((f - walk_f) / (1.0 - walk_f), 0.0, 1.0))
	if crouching and f <= dz:
		s *= 0.85
	# Firing error grows with each bullet of a spray and recovers when you stop shooting.
	s += minf(shots_in_burst, 8) * 0.28
	return s

func _physics_process(delta: float) -> void:
	var g := Settings.game()
	var input := Vector2.ZERO
	if can_move:
		if Input.is_physical_key_pressed(KEY_W): input.y -= 1.0
		if Input.is_physical_key_pressed(KEY_S): input.y += 1.0
		if Input.is_physical_key_pressed(KEY_A): input.x -= 1.0
		if Input.is_physical_key_pressed(KEY_D): input.x += 1.0
		walking = Input.is_physical_key_pressed(KEY_SHIFT)
		var want_crouch := Input.is_physical_key_pressed(KEY_CTRL)
		if want_crouch != crouching:
			crouching = want_crouch
			var tw := create_tween()
			tw.tween_property(cam, "position:y", float(g.get("eye_crouch", 1.15)) if crouching else float(g.get("eye", 1.6)), 0.12)
		if Input.is_physical_key_pressed(KEY_SPACE) and is_on_floor():
			velocity.y = 4.2
	var wish := (transform.basis * Vector3(input.x, 0, input.y))
	wish.y = 0
	wish = wish.normalized()
	var hv := Vector3(velocity.x, 0, velocity.z)
	var target := wish * max_speed()
	var friction := float(g.get("friction", 33.0))
	var accel := float(g.get("accel", 40.0))
	if wish == Vector3.ZERO:
		hv = hv.move_toward(Vector3.ZERO, friction * delta)
	else:
		# Pressing against current motion (counter-strafe) brakes with friction + acceleration.
		var against := hv.length() > 0.01 and hv.normalized().dot(wish) < -0.1
		hv = hv.move_toward(target, (accel + friction if against else accel) * delta)
	velocity.x = hv.x
	velocity.z = hv.z
	if not is_on_floor():
		velocity.y -= 12.0 * delta
	move_and_slide()
	# Stop timing: from "keys say stop / reverse" to reaching the accuracy dead-zone.
	var moving := speed_frac() > float(g.get("deadzone", 0.25))
	var braking := wish == Vector3.ZERO or (hv.length() > 0.01 and hv.normalized().dot(wish) < -0.1)
	if moving and braking and stop_started_ms < 0:
		stop_started_ms = Time.get_ticks_msec()
	elif not braking:
		stop_started_ms = -1
	if _was_moving and not moving and stop_started_ms >= 0:
		last_stop_ms = Time.get_ticks_msec() - stop_started_ms
		stop_started_ms = -1
	_was_moving = moving

func _process(delta: float) -> void:
	since_shot += delta
	fire_cooldown -= delta
	if since_shot > float(weapon()["recover"]):
		shots_in_burst = 0
	if reloading > 0.0:
		reloading -= delta
		if reloading <= 0.0:
			ammo = int(weapon()["mag"])
	if can_shoot and Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT) and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		fire()
	if can_shoot and Input.is_physical_key_pressed(KEY_R) and reloading <= 0.0 and ammo < int(weapon()["mag"]):
		reloading = float(weapon()["reload"])
	# Viewmodel: recoil kick + gentle walk bob (purely visual, bullets come from the crosshair).
	_kick = move_toward(_kick, 0.0, delta * 6.0)
	_bob += delta * speed_frac() * 9.0
	var bob := Vector3(sin(_bob) * 0.006, absf(cos(_bob)) * 0.006, 0) * clampf(speed_frac(), 0.0, 1.0)
	_viewmodel.position = Vector3(0.2, -0.2 - (0.1 if reloading > 0.0 else 0.0), -0.42 + _kick * 0.03) + bob
	_viewmodel.rotation_degrees.x = _kick * 6.0

func fire() -> void:
	if fire_cooldown > 0.0 or reloading > 0.0:
		return
	if ammo <= 0:
		Sfx.play("empty")
		fire_cooldown = 0.25
		reloading = float(weapon()["reload"])
		return
	fire_cooldown = 1.0 / float(weapon()["rps"])
	ammo -= 1
	var spread := spread_deg()
	var moving := speed_frac() > float(Settings.game().get("deadzone", 0.25))
	# Random point in a disc of radius `spread` (bullets leave the crosshair; VALORANT's crosshair does not move).
	var ang := _rng.randf() * TAU
	var r := spread * sqrt(_rng.randf())
	var climb := minf(shots_in_burst, 6) * 0.45   # spray climbs upward
	var off := Vector2(cos(ang) * r, sin(ang) * r + climb)
	shots_in_burst += 1
	since_shot = 0.0
	_kick = 1.0
	Sfx.play("shot", -6.0, _rng.randf_range(0.96, 1.04))
	var basis := cam.global_transform.basis
	var dir := (-basis.z).rotated(basis.y, deg_to_rad(-off.x)).rotated(basis.x, deg_to_rad(off.y)).normalized()
	var from := cam.global_position
	var q := PhysicsRayQueryParameters3D.create(from, from + dir * 300.0, 1 | 2)
	q.collide_with_areas = true
	q.collide_with_bodies = true
	q.exclude = [get_rid()]
	var hit := get_world_3d().direct_space_state.intersect_ray(q)
	var res := {"hit": false, "part": "", "bot": null, "moving": moving, "spread": spread, "point": from + dir * 300.0}
	if hit and hit.collider is Area3D and (hit.collider as Area3D).has_meta("bot"):
		var a: Area3D = hit.collider
		res["hit"] = true
		res["part"] = a.get_meta("part")
		res["bot"] = a.get_meta("bot")
		res["point"] = hit.position
	elif hit:
		res["point"] = hit.position
	shot.emit(res)
