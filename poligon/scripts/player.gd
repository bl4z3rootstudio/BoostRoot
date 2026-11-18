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
var _flash: OmniLight3D
var _flash_mesh: MeshInstance3D
var _tracer: MeshInstance3D
var _fx_t := 0.0
var _trigger_was_down := false

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
	# VALORANT: horizontal FOV fixed for every aspect ratio. CS2: vertical fixed (Hor+).
	cam.keep_aspect = Camera3D.KEEP_WIDTH if String(Settings.game().get("fov_axis", "h")) == "h" else Camera3D.KEEP_HEIGHT
	cam.fov = float(Settings.game().get("fov", 103.0))
	cam.near = 0.03
	cam.far = 400.0
	cam.position.y = float(Settings.game().get("eye", 1.6))
	add_child(cam)
	cam.current = true
	_build_viewmodel()
	_build_fx()
	ammo = int(weapon()["mag"])

## 1 = primary (rifle), 2 = secondary (pistol), 3 = knife — same keys as the game.
var slot := 1
var equip_t := 0.0
var ads := false
var _ammo_by_slot := {}
var _step_t := 0.0

func weapon() -> Dictionary:
	var g := Settings.game()
	if slot == 3:
		return g["knife"]
	if slot == 2:
		return g["weapons"][g["secondary"]]
	return Settings.weapon()

func switch_slot(s: int) -> void:
	if s == slot:
		return
	_ammo_by_slot[slot] = ammo
	slot = s
	ammo = int(_ammo_by_slot.get(s, weapon()["mag"]))
	reloading = 0.0
	shots_in_burst = 0
	equip_t = float(weapon().get("equip", 0.8))   # can't shoot while drawing the weapon
	ads = false
	_rebuild_viewmodel()
	Sfx.play("tick", -8.0)

func reset_weapons() -> void:
	_ammo_by_slot.clear()
	slot = 1
	equip_t = 0.0
	ads = false
	ammo = int(weapon()["mag"])
	reloading = 0.0
	_rebuild_viewmodel()

func _rebuild_viewmodel() -> void:
	for c in _viewmodel.get_children():
		if c != _flash and c != _flash_mesh:
			c.queue_free()
	_build_gun_meshes()

func _build_viewmodel() -> void:
	_viewmodel = Node3D.new()
	cam.add_child(_viewmodel)
	_viewmodel.position = Vector3(0.2, -0.2, -0.42)
	_viewmodel.scale = Vector3.ONE * 0.6
	_build_gun_meshes()

static var _vm_mats := {}

static func _vm_mat(key: String, c: Color, metal: float, rough: float) -> StandardMaterial3D:
	if not _vm_mats.has(key):
		var m := StandardMaterial3D.new()
		m.albedo_color = c
		m.metallic = metal
		m.roughness = rough
		_vm_mats[key] = m
	return _vm_mats[key]

## Simple but readable first-person models: rifle, pistol or knife, plus gloved hands.
func _build_gun_meshes() -> void:
	var metal := _vm_mat("metal", Color(0.16, 0.17, 0.19), 0.7, 0.35)
	var body := _vm_mat("body", Color(0.32, 0.33, 0.36), 0.4, 0.5)
	var wood := _vm_mat("accent", Color(0.78, 0.36, 0.2), 0.1, 0.55)
	var glove := _vm_mat("glove", Color(0.12, 0.12, 0.13), 0.0, 0.8)
	var sleeve := _vm_mat("sleeve", Color(0.24, 0.3, 0.42), 0.0, 0.85)
	var parts: Array = []
	var w := weapon()
	if w.get("melee", false):
		parts = [
			[Vector3(0.03, 0.035, 0.13), Vector3(0, -0.02, 0.06), glove],
			[Vector3(0.012, 0.04, 0.22), Vector3(0, 0.0, -0.11), _vm_mat("blade", Color(0.75, 0.78, 0.82), 0.9, 0.2)],
			[Vector3(0.06, 0.012, 0.02), Vector3(0, -0.01, -0.005), metal],
			[Vector3(0.07, 0.08, 0.1), Vector3(0, -0.04, 0.08), glove],
			[Vector3(0.08, 0.08, 0.3), Vector3(0.01, -0.06, 0.28), sleeve],
		]
	elif int(w["mag"]) <= 8:
		parts = [
			[Vector3(0.04, 0.06, 0.2), Vector3(0, 0.01, -0.06), body],
			[Vector3(0.03, 0.03, 0.1), Vector3(0, 0.03, -0.2), metal],
			[Vector3(0.036, 0.1, 0.05), Vector3(0, -0.06, 0.02), _vm_mat("grip", Color(0.25, 0.2, 0.17), 0.0, 0.7)],
			[Vector3(0.07, 0.08, 0.09), Vector3(0, -0.07, 0.05), glove],
			[Vector3(0.08, 0.08, 0.3), Vector3(0.01, -0.1, 0.25), sleeve],
		]
	else:
		parts = [
			[Vector3(0.05, 0.075, 0.36), Vector3(0, 0, 0), body],                 # receiver
			[Vector3(0.022, 0.022, 0.32), Vector3(0, 0.012, -0.33), metal],       # barrel
			[Vector3(0.046, 0.05, 0.16), Vector3(0, -0.005, -0.22), metal],       # handguard
			[Vector3(0.042, 0.13, 0.055), Vector3(0, -0.095, -0.02), metal],      # magazine
			[Vector3(0.04, 0.09, 0.045), Vector3(0, -0.075, 0.13), metal],        # grip
			[Vector3(0.05, 0.07, 0.2), Vector3(0, -0.015, 0.26), wood],           # stock
			[Vector3(0.018, 0.03, 0.05), Vector3(0, 0.055, -0.08), metal],        # front sight
			[Vector3(0.03, 0.025, 0.07), Vector3(0, 0.05, 0.07), metal],          # rear sight
			[Vector3(0.06, 0.07, 0.09), Vector3(-0.005, -0.075, 0.12), glove],    # right hand
			[Vector3(0.06, 0.06, 0.09), Vector3(-0.02, -0.04, -0.22), glove],     # left hand
			[Vector3(0.08, 0.08, 0.3), Vector3(0.01, -0.1, 0.3), sleeve],         # right forearm
		]
	for pr in parts:
		var mi := MeshInstance3D.new()
		var b := BoxMesh.new()
		b.size = pr[0]
		mi.mesh = b
		mi.position = pr[1]
		mi.material_override = pr[2]
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_viewmodel.add_child(mi)
	_viewmodel.rotation_degrees.y = 4.0

func _build_fx() -> void:
	_flash = OmniLight3D.new()
	_flash.light_color = Color(1.0, 0.75, 0.4)
	_flash.light_energy = 0.0
	_flash.omni_range = 4.0
	_flash.shadow_enabled = false
	_viewmodel.add_child(_flash)
	_flash.position = Vector3(0, 0.01, -0.55)
	_flash_mesh = MeshInstance3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(0.12, 0.12)
	_flash_mesh.mesh = q
	var fm := StandardMaterial3D.new()
	fm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	fm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	fm.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	fm.albedo_color = Color(1.0, 0.7, 0.3, 0.9)
	fm.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	_flash_mesh.material_override = fm
	_flash_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_flash_mesh.visible = false
	_viewmodel.add_child(_flash_mesh)
	_flash_mesh.position = Vector3(0, 0.01, -0.52)
	_tracer = MeshInstance3D.new()
	var c := CylinderMesh.new()
	c.top_radius = 0.006
	c.bottom_radius = 0.006
	c.height = 1.0
	c.radial_segments = 4
	c.rings = 1
	_tracer.mesh = c
	var tm := StandardMaterial3D.new()
	tm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	tm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	tm.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	tm.albedo_color = Color(1.0, 0.85, 0.55, 0.55)
	_tracer.material_override = tm
	_tracer.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_tracer.top_level = true
	_tracer.visible = false
	add_child(_tracer)

## Muzzle flash + a short tracer from the gun to where the bullet landed (visual only).
func _show_fx(to: Vector3) -> void:
	_fx_t = 0.045
	_flash.light_energy = 2.5
	_flash_mesh.visible = true
	_flash_mesh.rotation.z = _rng.randf() * TAU
	var from := _flash_mesh.global_position
	var mid := (from + to) * 0.5
	var len := from.distance_to(to)
	if len > 0.5:
		_tracer.global_transform = Transform3D(Basis(), mid)
		_tracer.look_at(to, Vector3.UP if absf((to - from).normalized().y) < 0.99 else Vector3.RIGHT)
		_tracer.rotate_object_local(Vector3.RIGHT, PI / 2.0)
		_tracer.scale = Vector3(1, len, 1)
		_tracer.visible = true

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
	if ads:
		# Zoomed: sensitivity × the game's zoom multiplier, scaled down by the zoom like the game does.
		k *= float(Settings.data.get("ads_mult", 1.0)) / float(weapon().get("zoom", 1.0))
	yaw -= counts.x * k
	pitch = clampf(pitch - counts.y * k, -89.0, 89.0)
	_apply_look()

func max_speed() -> float:
	var g := Settings.game()
	var s := float(weapon().get("run", 5.4))
	if ads:
		s *= 0.76
	if crouching:
		s *= float(g.get("crouch_mult", 0.5))
	elif walking:
		s *= float(g.get("walk_mult", 0.6))
	return s

func speed_frac() -> float:
	var hv := Vector2(velocity.x, velocity.z).length()
	# Accuracy is judged against the gun's own top speed (knife-running does not count as "slow").
	return hv / float((Settings.weapon() if slot == 3 else weapon()).get("run", 5.4))

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
	if ads:
		s *= 0.65
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
	equip_t -= delta
	if can_shoot:
		if Input.is_physical_key_pressed(KEY_1): switch_slot(1)
		elif Input.is_physical_key_pressed(KEY_2): switch_slot(2)
		elif Input.is_physical_key_pressed(KEY_3): switch_slot(3)
	# Right mouse: aim down sights (zoom), like the game's alt-fire on rifles / Sheriff.
	var want_ads := can_shoot and Input.is_mouse_button_pressed(MOUSE_BUTTON_RIGHT) and float(weapon().get("zoom", 1.0)) > 1.01 \
		and reloading <= 0.0 and equip_t <= 0.0
	if want_ads != ads:
		ads = want_ads
	var base_fov := float(Settings.game().get("fov", 103.0))
	var target_fov := rad_to_deg(2.0 * atan(tan(deg_to_rad(base_fov) / 2.0) / float(weapon().get("zoom", 1.0)))) if ads else base_fov
	cam.fov = move_toward(cam.fov, target_fov, delta * 220.0)
	# Footsteps when running (shift-walk and crouch are silent, as in the game).
	_step_t -= delta
	if is_on_floor() and not walking and not crouching and speed_frac() > 0.6 and _step_t <= 0.0:
		_step_t = 0.36
		Sfx.play("step", -14.0, _rng.randf_range(0.9, 1.1))
	fire_cooldown -= delta
	if since_shot > float(weapon()["recover"]):
		shots_in_burst = 0
	if reloading > 0.0:
		reloading -= delta
		if reloading <= 0.0:
			ammo = int(weapon()["mag"])
	var down := Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT)
	if can_shoot and down and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED and (bool(weapon().get("auto", true)) or not _trigger_was_down):
		fire()
	_trigger_was_down = down
	if can_shoot and Input.is_physical_key_pressed(KEY_R) and reloading <= 0.0 and ammo < int(weapon()["mag"]):
		reloading = float(weapon()["reload"])
	if _fx_t > 0.0:
		_fx_t -= delta
		if _fx_t <= 0.0:
			_flash.light_energy = 0.0
			_flash_mesh.visible = false
			_tracer.visible = false
	# Viewmodel: recoil kick + gentle walk bob (purely visual, bullets come from the crosshair).
	_kick = move_toward(_kick, 0.0, delta * 6.0)
	_bob += delta * speed_frac() * 9.0
	var bob := Vector3(sin(_bob) * 0.006, absf(cos(_bob)) * 0.006, 0) * clampf(speed_frac(), 0.0, 1.0)
	_viewmodel.position = Vector3(0.2, -0.2 - (0.1 if reloading > 0.0 else 0.0), -0.42 + _kick * 0.03) + bob
	_viewmodel.rotation_degrees.x = _kick * 6.0

func fire() -> void:
	if fire_cooldown > 0.0 or reloading > 0.0 or equip_t > 0.0 or weapon().get("melee", false):
		return
	if ammo <= 0:
		Sfx.play("empty")
		fire_cooldown = 0.25
		reloading = float(weapon()["reload"])
		return
	fire_cooldown = 1.0 / (float(weapon()["rps"]) * (0.9 if ads else 1.0))
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
		res["dist"] = from.distance_to(hit.position)
	elif hit:
		res["point"] = hit.position
	_show_fx(res["point"])
	shot.emit(res)
