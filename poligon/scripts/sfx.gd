extends Node
## All sounds are synthesised at start-up (no audio files to ship or license).

const RATE := 44100
var sounds := {}
var _players: Array[AudioStreamPlayer] = []

func _ready() -> void:
	sounds["shot"] = _make(0.22, func(t: float, n: float) -> float:
		var crack := n * exp(-t * 55.0)
		var body := sin(TAU * 95.0 * t) * exp(-t * 18.0) * 0.8
		return clampf((crack * 0.9 + body) * 0.9, -1.0, 1.0))
	sounds["head"] = _make(0.32, func(t: float, n: float) -> float:
		return (sin(TAU * 1850.0 * t) * 0.6 + sin(TAU * 2780.0 * t) * 0.3) * exp(-t * 14.0) + n * exp(-t * 120.0) * 0.2)
	sounds["body"] = _make(0.12, func(t: float, n: float) -> float:
		return (sin(TAU * 180.0 * t) * 0.7 + n * 0.3) * exp(-t * 35.0))
	sounds["kill"] = _make(0.28, func(t: float, _n: float) -> float:
		var f := 660.0 if t < 0.09 else 990.0
		return sin(TAU * f * t) * 0.45 * exp(-fmod(t, 0.09) * 10.0))
	sounds["enemy"] = _make(0.3, func(t: float, n: float) -> float:
		return clampf(n * exp(-t * 30.0) * 1.2 + sin(TAU * 70.0 * t) * exp(-t * 10.0), -1.0, 1.0))
	sounds["step"] = _make(0.09, func(t: float, n: float) -> float:
		return (n * 0.6 + sin(TAU * 120.0 * t) * 0.5) * exp(-t * 60.0))
	sounds["tick"] = _make(0.05, func(t: float, _n: float) -> float:
		return sin(TAU * 1200.0 * t) * exp(-t * 80.0) * 0.4)
	sounds["empty"] = _make(0.06, func(t: float, n: float) -> float:
		return n * exp(-t * 90.0) * 0.4)
	for i in 8:
		var p := AudioStreamPlayer.new()
		add_child(p)
		_players.append(p)

func _make(seconds: float, gen: Callable) -> AudioStreamWAV:
	var count := int(seconds * RATE)
	var bytes := PackedByteArray()
	bytes.resize(count * 2)
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	for i in count:
		var v: float = gen.call(float(i) / RATE, rng.randf_range(-1.0, 1.0))
		bytes.encode_s16(i * 2, int(clampf(v, -1.0, 1.0) * 32000.0))
	var w := AudioStreamWAV.new()
	w.format = AudioStreamWAV.FORMAT_16_BITS
	w.mix_rate = RATE
	w.stereo = false
	w.data = bytes
	return w

func play(name: String, volume_db := 0.0, pitch := 1.0) -> void:
	if not sounds.has(name):
		return
	for p in _players:
		if not p.playing:
			p.stream = sounds[name]
			p.volume_db = volume_db
			p.pitch_scale = pitch
			p.play()
			return
	_players[0].stream = sounds[name]
	_players[0].play()

## Positional sound (e.g. enemy footsteps behind you for the turn drill).
func play_at(name: String, parent: Node3D, pos: Vector3, volume_db := 0.0) -> void:
	if not sounds.has(name):
		return
	var p := AudioStreamPlayer3D.new()
	p.stream = sounds[name]
	p.volume_db = volume_db
	p.unit_size = 6.0
	parent.add_child(p)
	p.global_position = pos
	p.finished.connect(p.queue_free)
	p.play()
