class_name Bot
extends Node3D
## Humanoid training bot with VALORANT-sized hitboxes (head / body / legs) and the red enemy outline.

signal died(bot: Bot)

var hp := 150
var alive := true
var spawned_at := 0.0          # Time.get_ticks_msec() when it became visible
var deadline_ms := 0           # 0 = never shoots back
var shots_taken := 0
var head_hit := false
# Movement
var path_from := Vector3.ZERO
var path_to := Vector3.ZERO
var move_t := 1.0
var move_speed := 5.4
var strafe := false
var strafe_dir := 1.0
var strafe_center := Vector3.ZERO
var strafe_width := 2.0
var strafe_timer := 0.0
var jiggles := 0              # peek-and-pull-back repeats before committing (jiggle peek)
var _hold := 0.0
var _flash := 0.0
var _mats: Array[StandardMaterial3D] = []
var _rng := RandomNumberGenerator.new()

static var _shared := {}

static func _mat(key: String, color: Color, outline := false) -> StandardMaterial3D:
	if _shared.has(key):
		return _shared[key]
	var m := StandardMaterial3D.new()
	m.albedo_color = color
	if outline:
		m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		m.cull_mode = BaseMaterial3D.CULL_FRONT
		m.grow = true
		m.grow_amount = 0.018
	else:
		m.roughness = 0.55
		m.metallic = 0.15
	_shared[key] = m
	return m

func _ready() -> void:
	_rng.randomize()
	var body_col := Color(0.16, 0.17, 0.21)
	var accent := Color(0.95, 0.27, 0.33)
	hp = int(Settings.game().get("hp", 150))
	var head_y: float = Settings.game().get("head_y", 1.6)
	var head_r: float = Settings.game().get("head_r", 0.125)
	# Legs
	for sx in [-0.11, 0.11]:
		_part(_capsule(0.085, 0.82), Vector3(sx, 0.43, 0), body_col)
	# Torso + shoulders + arms
	_part(_capsule(0.2, 0.62, 0.17), Vector3(0, 1.12, 0), body_col)
	_part(_box(Vector3(0.5, 0.12, 0.24)), Vector3(0, 1.38, 0), body_col)
	for sx in [-0.29, 0.29]:
		_part(_capsule(0.06, 0.62), Vector3(sx, 1.1, 0.02), body_col)
	# Chest stripe (team colour, readable at range)
	_part(_box(Vector3(0.3, 0.05, 0.02)), Vector3(0, 1.25, 0.17), accent, false)
	# Neck + head + visor
	_part(_capsule(0.06, 0.14), Vector3(0, head_y - head_r - 0.04, 0), body_col)
	var head := SphereMesh.new()
	head.radius = head_r
	head.height = head_r * 2.0
	_part(head, Vector3(0, head_y, 0), Color(0.22, 0.23, 0.28))
	_part(_box(Vector3(head_r * 1.5, head_r * 0.45, 0.03)), Vector3(0, head_y + 0.01, head_r * 0.85), accent, false)
	# Hitboxes
	_hitbox("head", _sphere_shape(head_r), Vector3(0, head_y, 0))
	_hitbox("body", _capsule_shape(0.24, 0.8), Vector3(0, 1.12, 0))
	_hitbox("legs", _box_shape(Vector3(0.4, 0.78, 0.25)), Vector3(0, 0.4, 0))

func _capsule(r: float, h: float, depth := -1.0) -> Mesh:
	var m := CapsuleMesh.new()
	m.radius = r
	m.height = h
	m.radial_segments = 16
	m.rings = 6
	return m

func _box(s: Vector3) -> Mesh:
	var m := BoxMesh.new()
	m.size = s
	return m

func _part(mesh: Mesh, pos: Vector3, color: Color, outline := true) -> void:
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	var m := StandardMaterial3D.new()
	m.albedo_color = color
	m.roughness = 0.6
	m.metallic = 0.1
	m.emission_enabled = true
	m.emission = Color.WHITE
	m.emission_energy_multiplier = 0.0
	_mats.append(m)
	mi.material_override = m
	mi.position = pos
	if outline:
		mi.material_overlay = _mat("outline", Color(1.0, 0.18, 0.28), true)
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(mi)

func _sphere_shape(r: float) -> Shape3D:
	var s := SphereShape3D.new()
	s.radius = r
	return s

func _capsule_shape(r: float, h: float) -> Shape3D:
	var s := CapsuleShape3D.new()
	s.radius = r
	s.height = h
	return s

func _box_shape(size: Vector3) -> Shape3D:
	var s := BoxShape3D.new()
	s.size = size
	return s

func _hitbox(part: String, shape: Shape3D, pos: Vector3) -> void:
	var a := Area3D.new()
	a.collision_layer = 2
	a.collision_mask = 0
	a.monitoring = false
	a.set_meta("part", part)
	a.set_meta("bot", self)
	var cs := CollisionShape3D.new()
	cs.shape = shape
	a.add_child(cs)
	a.position = pos
	add_child(a)

func head_position_at(p: Vector3) -> Vector3:
	return p + Vector3(0, float(Settings.game().get("head_y", 1.6)) * scale.y, 0)

func head_position() -> Vector3:
	return head_position_at(global_position)

## Walks from `from` to `to` (a peek). Visible time starts now.
func peek(from: Vector3, to: Vector3, speed: float) -> void:
	path_from = from
	path_to = to
	global_position = from
	move_speed = maxf(speed, 0.1)
	move_t = 0.0

func start_strafe(center: Vector3, width: float, speed: float) -> void:
	strafe = true
	strafe_center = center
	strafe_width = width
	move_speed = speed
	global_position = center
	strafe_timer = _rng.randf_range(0.25, 0.7)

func face(target: Vector3) -> void:
	var t := Vector3(target.x, global_position.y, target.z)
	if t.distance_to(global_position) > 0.01:
		look_at(t, Vector3.UP, true)

## Returns damage dealt; kills at hp <= 0.
func hit(part: String, weapon: Dictionary) -> int:
	if not alive:
		return 0
	var dmg := int(weapon.get(part, 40))
	hp -= dmg
	if part == "head":
		head_hit = true
	_flash = 0.12
	if hp <= 0:
		_die()
	return dmg

func _die() -> void:
	alive = false
	strafe = false
	move_t = 1.0
	for c in get_children():
		if c is Area3D:
			c.queue_free()
	died.emit(self)
	var tw := create_tween()
	tw.set_parallel(true)
	tw.tween_property(self, "rotation:x", -PI / 2.2, 0.35).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
	tw.tween_property(self, "position:y", position.y - 0.2, 0.35)
	tw.chain().tween_interval(0.35)
	tw.chain().tween_callback(queue_free)

func _physics_process(delta: float) -> void:
	if not alive:
		return
	if move_t < 1.0:
		var len := path_from.distance_to(path_to)
		move_t = 1.0 if len < 0.001 else minf(1.0, move_t + move_speed * delta / len)
		global_position = path_from.lerp(path_to, move_t)
		if move_t >= 1.0 and jiggles > 0:
			# Jiggle: pull back behind cover, then come out again.
			jiggles -= 1
			var back := path_from
			path_from = path_to
			path_to = back
			_hold = _rng.randf_range(0.12, 0.3)
		if move_t >= 1.0 and _rng.randf() < 0.25:
			Sfx.play_at("step", get_parent(), global_position, -12.0)
	elif _hold > 0.0:
		_hold -= delta
		if _hold <= 0.0:
			move_t = 0.0
	elif strafe:
		strafe_timer -= delta
		if strafe_timer <= 0.0:
			# VALORANT-style A-D: switch direction, sometimes stop dead (that's when it's accurate and you should shoot).
			var r := _rng.randf()
			strafe_dir = 0.0 if r < 0.25 else (-strafe_dir if strafe_dir != 0.0 else (1.0 if r < 0.62 else -1.0))
			strafe_timer = _rng.randf_range(0.18, 0.75)
		var right := global_transform.basis.x
		var next := global_position + right * strafe_dir * move_speed * delta
		var off := (next - strafe_center).dot(right)
		if absf(off) > strafe_width:
			strafe_dir = -strafe_dir
		else:
			global_position = next
	if _flash > 0.0:
		_flash -= delta
		var e := 2.5 if _flash > 0.0 else 0.0
		for m in _mats:
			m.emission_energy_multiplier = e
