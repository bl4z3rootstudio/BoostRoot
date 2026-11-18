class_name RangeWorld
extends Node3D
## The practice range: lit floor with a metre grid, distance markers, cover boxes to peek from, back walls.

## Cover pieces enemies peek from: {pos, size}
var covers: Array[Dictionary] = []

const GRID_SHADER := """
shader_type spatial;
render_mode specular_schlick_ggx;
// Range floor: poured concrete with 2 m slabs, painted lane lines and a warm tint.
uniform vec3 base : source_color = vec3(0.36, 0.36, 0.36);
uniform vec3 seam : source_color = vec3(0.24, 0.24, 0.25);
uniform vec3 paint : source_color = vec3(0.95, 0.72, 0.18);
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float line_at(float v, float w) { float d = abs(v) / fwidth(v); return 1.0 - clamp(d / w, 0.0, 1.0); }
void fragment() {
	vec2 p = wpos.xz;
	vec2 cell = floor(p / 2.0);
	float n = hash(cell) * 0.06 + hash(floor(p * 6.0)) * 0.025;
	vec2 g = abs(fract(p / 2.0 - 0.5) - 0.5) * 2.0;
	float seams = 1.0 - clamp(min(g.x, g.y) / (fwidth(p.x) * 1.2), 0.0, 1.0);
	vec3 c = mix(base * (0.94 + n), seam, seams * 0.6);
	// Lane lines every 6 m towards the targets, and a firing line at the spawn.
	float lanes = line_at(fract((p.x + 3.0) / 6.0) - 0.5, 3.0) * step(p.y, 2.0) * step(-55.0, p.y);
	float fire_line = 1.0 - smoothstep(0.0, 0.08, abs(p.y + 1.5));
	c = mix(c, paint, max(lanes * 0.85, fire_line * step(abs(p.x), 12.0)));
	ALBEDO = c;
	ROUGHNESS = 0.9;
}
"""

func build() -> void:
	# Environment: soft sky light, ACES tonemap, light glow. Kept cheap so the range runs at hundreds of FPS.
	var env := Environment.new()
	var sky := Sky.new()
	var sm := ProceduralSkyMaterial.new()
	sm.sky_top_color = Color(0.22, 0.45, 0.78)
	sm.sky_horizon_color = Color(0.78, 0.84, 0.9)
	sm.ground_bottom_color = Color(0.1, 0.1, 0.12)
	sm.ground_horizon_color = Color(0.5, 0.52, 0.56)
	sky.sky_material = sm
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = 0.55
	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.tonemap_exposure = 1.05
	env.glow_enabled = true
	env.glow_intensity = 0.25
	env.ssao_enabled = false
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-52, -35, 0)
	sun.light_energy = 1.1
	sun.light_color = Color(1.0, 0.96, 0.9)
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 70.0
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_ORTHOGONAL
	add_child(sun)
	# Floor
	var floor_mat := ShaderMaterial.new()
	var sh := Shader.new()
	sh.code = GRID_SHADER
	floor_mat.shader = sh
	_solid(Vector3(160, 0.2, 160), Vector3(0, -0.1, -20), floor_mat)
	# Distance markers every 10 m (like the VALORANT range lanes)
	for d in [10, 20, 30, 40]:
		var lbl := Label3D.new()
		lbl.text = "%d m" % d
		lbl.font_size = 96
		lbl.pixel_size = 0.012
		lbl.modulate = Color(0.98, 0.78, 0.2, 0.9)
		lbl.basis = Basis(Vector3(1, 0, 0), Vector3(0, 0, -1), Vector3(0, 1, 0))  # lying on the floor, readable from the spawn
		lbl.position = Vector3(0, 0.02, -d)
		lbl.shaded = false
		add_child(lbl)
	# Walls around the play space
	var wall := _mat(Color(0.6, 0.59, 0.56), 0.95)
	_solid(Vector3(120, 9, 1), Vector3(0, 4.5, -62), wall)
	_solid(Vector3(1, 9, 90), Vector3(-45, 4.5, -18), wall)
	_solid(Vector3(1, 9, 90), Vector3(45, 4.5, -18), wall)
	_solid(Vector3(90, 9, 1), Vector3(0, 4.5, 26), wall)
	var stripe := _mat(Color(0.95, 0.45, 0.2), 0.6)
	var teal := _mat(Color(0.2, 0.7, 0.72), 0.6)
	_deco(Vector3(120, 0.5, 0.1), Vector3(0, 2.2, -61.45), stripe)
	_deco(Vector3(120, 0.18, 0.1), Vector3(0, 2.65, -61.45), teal)
	_deco(Vector3(0.1, 0.5, 90), Vector3(-44.45, 2.2, -18), stripe)
	_deco(Vector3(0.1, 0.5, 90), Vector3(44.45, 2.2, -18), stripe)
	var base_mat := _mat(Color(0.25, 0.25, 0.27), 0.9)
	_deco(Vector3(120, 0.4, 0.2), Vector3(0, 0.2, -61.4), base_mat)
	# Cover boxes in an arc in front of the player — enemies peek out of these.
	var crate := _mat(Color(0.72, 0.4, 0.17), 0.75)
	var concrete := _mat(Color(0.48, 0.49, 0.52), 0.9)
	var spots := [
		[-14, -12, 2.2], [-6, -16, 2.6], [5, -14, 2.2], [13, -11, 2.4],
		[-20, -22, 3.0], [-9, -27, 2.8], [3, -30, 3.0], [15, -24, 2.8], [24, -18, 2.6],
		[-26, -34, 3.2], [-12, -40, 3.0], [10, -42, 3.2], [27, -36, 3.0],
	]
	var i := 0
	for s in spots:
		var size := Vector3(2.2, s[2], 1.6) if i % 3 != 0 else Vector3(3.2, s[2], 1.2)
		var pos := Vector3(s[0], size.y / 2.0, s[1])
		# Face the spawn point so the box fully hides an enemy until it steps out of the side.
		_solid(size, pos, crate if i % 2 == 0 else concrete, atan2(pos.x, pos.z))
		covers.append({"pos": pos, "size": size})
		i += 1
	# A few pillars for depth
	for p in [Vector3(-32, 3, -10), Vector3(32, 3, -8), Vector3(-36, 3, -46), Vector3(36, 3, -48)]:
		_solid(Vector3(1.4, 6, 1.4), p, concrete)

## Visual-only box (no collision).
func _deco(size: Vector3, pos: Vector3, mat: Material) -> void:
	var mi := MeshInstance3D.new()
	var b := BoxMesh.new()
	b.size = size
	mi.mesh = b
	mi.material_override = mat
	mi.position = pos
	add_child(mi)

func _mat(c: Color, rough: float) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = rough
	return m

func _solid(size: Vector3, pos: Vector3, mat: Material, yaw := 0.0) -> void:
	var body := StaticBody3D.new()
	body.collision_layer = 1
	body.collision_mask = 0
	var cs := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = size
	cs.shape = shape
	body.add_child(cs)
	var mi := MeshInstance3D.new()
	var b := BoxMesh.new()
	b.size = size
	mi.mesh = b
	mi.material_override = mat
	body.add_child(mi)
	body.position = pos
	body.rotation.y = yaw
	add_child(body)

## Hidden and visible positions for a peek out of a cover's left or right side, as seen from `viewer`.
func peek_points(cover: Dictionary, viewer: Vector3, right_side: bool, step: float) -> Array[Vector3]:
	var pos: Vector3 = cover["pos"]
	var size: Vector3 = cover["size"]
	var to_viewer := (viewer - pos)
	to_viewer.y = 0
	to_viewer = to_viewer.normalized()
	var side := to_viewer.cross(Vector3.UP).normalized() * (1.0 if right_side else -1.0)
	var behind := pos - to_viewer * (size.z / 2.0 + 0.6)
	behind.y = 0
	var edge := size.x / 2.0
	var hidden := behind + side * (edge - 0.35)
	var shown := behind + side * (edge + step)
	return [hidden, shown]
