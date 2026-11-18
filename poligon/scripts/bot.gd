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
var _rng := RandomNumberGenerator.new()

static var _shared := {}
static var _meshes := {}
var _parts: Array[MeshInstance3D] = []
const BASE_HEAD_Y := 1.6

static func _mat(key: String, color: Color, outline := false) -> StandardMaterial3D:
	if _shared.has(key):
		return _shared[key]
	var m := StandardMaterial3D.new()
	m.albedo_color = color
	if outline:
		# VALORANT-style red enemy highlight (inverted hull).
		m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		m.cull_mode = BaseMaterial3D.CULL_FRONT
		m.grow = true
		m.grow_amount = 0.012
	else:
		m.roughness = 0.62
		m.metallic = 0.25
	_shared[key] = m
	return m

func _ready() -> void:
	_rng.randomize()
	hp = int(Settings.game().get("hp", 150))
	# Built at VALORANT size; other games scale the whole body so the head sits at their real height.
	scale = Vector3.ONE * (float(Settings.game().get("head_y", BASE_HEAD_Y)) / BASE_HEAD_Y)
	_spawn_robot_mesh()
	var head_y := BASE_HEAD_Y
	var head_r := 0.125
	# Hitboxes (unchanged sizes: they are what the game rules are about)
	_hitbox("head", _sphere_shape(head_r), Vector3(0, head_y, 0))
	_hitbox("body", _capsule_shape(0.24, 0.8), Vector3(0, 1.12, 0))
	_hitbox("legs", _box_shape(Vector3(0.4, 0.78, 0.25)), Vector3(0, 0.4, 0))

static var _robot: ArrayMesh = null
var _pending: Array = []

## All bots share one merged mesh (one draw per material instead of ~50 per bot) → high FPS with many bots.
func _spawn_robot_mesh() -> void:
	if _robot == null:
		_pending.clear()
		_build_robot()
		var by_mat := {}
		for e in _pending:
			var key: Material = e[2]
			if not by_mat.has(key):
				var st := SurfaceTool.new()
				st.begin(Mesh.PRIMITIVE_TRIANGLES)
				by_mat[key] = st
			(by_mat[key] as SurfaceTool).append_from(e[0], 0, e[1])
		_robot = ArrayMesh.new()
		for m in by_mat.keys():
			var st: SurfaceTool = by_mat[m]
			st.set_material(m)
			st.commit(_robot)
		_pending.clear()
	var mi := MeshInstance3D.new()
	mi.mesh = _robot
	mi.material_overlay = _mat("outline", Color(1.0, 0.18, 0.28), true)
	add_child(mi)
	_parts.append(mi)

## Training robot: segmented grey shell, dark joints (original design in the style of a range bot).
func _build_robot() -> void:
	var shell := _mat("shell", Color(0.5, 0.51, 0.54))
	var joint := _mat("joint", Color(0.3, 0.31, 0.34))
	var dark := _mat("dark", Color(0.08, 0.08, 0.1))
	var accent := _mat("accent", Color(0.95, 0.45, 0.18))
	var glow := _glow_mat()
	# --- head: rounded capsule, eye sensors, ear discs
	_add(_capsule(0.105, 0.27), Vector3(0, 1.62, 0), shell)
	_add(_box(Vector3(0.12, 0.035, 0.03)), Vector3(0, 1.635, 0.092), dark)          # visor
	for sx in [-0.032, 0.032]:
		_add(_sphere(0.014), Vector3(sx, 1.635, 0.105), glow)                       # glowing eyes
	for sx in [-0.112, 0.112]:
		_add(_cyl(0.035, 0.03), Vector3(sx, 1.62, 0), joint, Vector3(0, 0, 90))
	# --- neck (spring look)
	for i in 3:
		_add(_cyl(0.045 - i * 0.004, 0.018), Vector3(0, 1.455 + i * 0.03, 0), joint)
	# --- torso: chest block + angled front plate + emblem, segmented abdomen, pelvis
	_add(_box(Vector3(0.4, 0.3, 0.24)), Vector3(0, 1.28, 0), shell)
	_add(_box(Vector3(0.3, 0.2, 0.06)), Vector3(0, 1.27, 0.12), shell, Vector3(-8, 0, 0))
	_add(_box(Vector3(0.05, 0.03, 0.02)), Vector3(0, 1.33, 0.155), glow)
	_add(_box(Vector3(0.41, 0.03, 0.245)), Vector3(0, 1.17, 0), accent)              # waist stripe
	_add(_box(Vector3(0.26, 0.08, 0.17)), Vector3(0, 1.1, 0), joint)
	_add(_box(Vector3(0.24, 0.06, 0.16)), Vector3(0, 1.03, 0), shell)
	_add(_box(Vector3(0.32, 0.1, 0.2)), Vector3(0, 0.94, 0), shell)
	_add(_box(Vector3(0.12, 0.1, 0.05)), Vector3(0, 0.87, 0.08), joint, Vector3(12, 0, 0))
	# --- arms
	for side in [-1.0, 1.0]:
		var x: float = 0.255 * side
		_add(_box(Vector3(0.13, 0.16, 0.18)), Vector3(x + 0.02 * side, 1.37, 0), shell)          # shoulder pad
		_add(_box(Vector3(0.135, 0.03, 0.185)), Vector3(x + 0.02 * side, 1.43, 0), accent)
		_add(_cyl(0.04, 0.06), Vector3(x, 1.27, 0), joint, Vector3(0, 0, 90))
		_add(_box(Vector3(0.07, 0.24, 0.07)), Vector3(x + 0.005 * side, 1.13, 0), shell)          # upper arm
		_add(_cyl(0.038, 0.08), Vector3(x + 0.01 * side, 0.995, 0), joint, Vector3(0, 0, 90))    # elbow
		_add(_box(Vector3(0.08, 0.22, 0.08)), Vector3(x + 0.012 * side, 0.87, 0.01), shell)      # forearm
		_add(_box(Vector3(0.095, 0.05, 0.095)), Vector3(x + 0.012 * side, 0.93, 0.01), joint)  # cuff
		_add(_box(Vector3(0.045, 0.09, 0.08)), Vector3(x + 0.014 * side, 0.715, 0.015), joint)  # hand
		for f in 3:
			_add(_box(Vector3(0.012, 0.06, 0.014)), Vector3(x + 0.014 * side, 0.645, -0.015 + f * 0.022), joint)
	# --- legs
	for side in [-1.0, 1.0]:
		var x: float = 0.1 * side
		_add(_box(Vector3(0.12, 0.08, 0.13)), Vector3(x, 0.855, 0), joint)                       # hip
		_add(_box(Vector3(0.1, 0.32, 0.11)), Vector3(x, 0.69, 0), shell)                          # thigh
		_add(_cyl(0.05, 0.12), Vector3(x, 0.52, 0), joint, Vector3(0, 0, 90))                     # knee
		_add(_box(Vector3(0.08, 0.07, 0.04)), Vector3(x, 0.52, 0.055), shell)                     # knee cap
		_add(_box(Vector3(0.085, 0.36, 0.09)), Vector3(x, 0.31, 0), shell)                        # shin
		_add(_cyl(0.035, 0.1), Vector3(x, 0.115, 0), joint, Vector3(0, 0, 90))                   # ankle
		_add(_box(Vector3(0.11, 0.06, 0.22)), Vector3(x, 0.035, 0.04), shell)                     # foot

func _add(mesh: Mesh, pos: Vector3, mat: Material, rot_deg := Vector3.ZERO) -> void:
	var b := Basis.from_euler(Vector3(deg_to_rad(rot_deg.x), deg_to_rad(rot_deg.y), deg_to_rad(rot_deg.z)))
	_pending.append([mesh, Transform3D(b, pos), mat])

func _capsule(r: float, h: float) -> Mesh:
	var key := "cap%.3f_%.3f" % [r, h]
	if not _meshes.has(key):
		var m := CapsuleMesh.new()
		m.radius = r
		m.height = h
		m.radial_segments = 14
		m.rings = 4
		_meshes[key] = m
	return _meshes[key]

func _cyl(r: float, h: float) -> Mesh:
	var key := "cyl%.3f_%.3f" % [r, h]
	if not _meshes.has(key):
		var m := CylinderMesh.new()
		m.top_radius = r
		m.bottom_radius = r
		m.height = h
		m.radial_segments = 12
		m.rings = 1
		_meshes[key] = m
	return _meshes[key]

func _sphere(r: float) -> Mesh:
	var key := "sph%.3f" % r
	if not _meshes.has(key):
		var m := SphereMesh.new()
		m.radius = r
		m.height = r * 2.0
		m.radial_segments = 8
		m.rings = 4
		_meshes[key] = m
	return _meshes[key]

func _box(size: Vector3) -> Mesh:
	var key := "box%s" % str(size)
	if not _meshes.has(key):
		var m := BoxMesh.new()
		m.size = size
		_meshes[key] = m
	return _meshes[key]

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

static func _glow_mat() -> Material:
	if not _shared.has("glow"):
		var m := StandardMaterial3D.new()
		m.albedo_color = Color(0.3, 0.95, 1.0)
		m.emission_enabled = true
		m.emission = Color(0.3, 0.95, 1.0)
		m.emission_energy_multiplier = 2.5
		_shared["glow"] = m
	return _shared["glow"]

static func _flash_mat() -> Material:
	if not _shared.has("flash"):
		var m := StandardMaterial3D.new()
		m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		m.albedo_color = Color(1, 0.35, 0.35, 0.45)
		_shared["flash"] = m
	return _shared["flash"]

func head_position_at(p: Vector3) -> Vector3:
	return p + Vector3(0, BASE_HEAD_Y * scale.y, 0)

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
func hit(part: String, weapon: Dictionary, dist := 10.0) -> int:
	if not alive:
		return 0
	var dmg := GameDefs.damage(weapon, part, dist)
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
		var was := _flash
		_flash -= delta
		if was == 0.12 or _flash <= 0.0:
			var ov: Material = _flash_mat() if _flash > 0.0 else _mat("outline", Color(1.0, 0.18, 0.28), true)
			for p in _parts:
				p.material_overlay = ov
