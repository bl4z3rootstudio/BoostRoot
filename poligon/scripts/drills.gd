class_name Drills
## The training modes. Text is Turkish (the app's main language); ids are stable (BoostRoot reads them from results.json).

const LIST := [
	{"id": "duel", "name": "Tepki Düellosu", "icon": "⚔",
		"desc": "Düşman bir siperin arkasından çıkar. Süren dolmadan kafasından vur; yoksa o seni vurur ve oyun biter. Gerçek maçtaki ilk çatışma.",
		"timed": false},
	{"id": "flick", "name": "Flick & Headshot", "icon": "◎",
		"desc": "Her yerde tek tek beliren düşmanlara hızlı nişan alıp tek mermiyle kafadan vur. Flick hızını ve isabetini geliştirir.",
		"timed": true},
	{"id": "micro", "name": "Mikro Ayar", "icon": "·",
		"desc": "Yakın ve birbirine yakın kafalar. Küçük, hassas fare hareketlerini ve art arda tıklamayı çalıştırır.",
		"timed": true},
	{"id": "turn", "name": "Köre Dönme", "icon": "↺",
		"desc": "Düşman arkandan ya da yanından gelir; ayak sesinden yönünü bul, dön ve vur. Arkadan gelen sürprizlere tepki.",
		"timed": false, "rounds": 20},
	{"id": "strafe", "name": "A-D Düellosu", "icon": "⇆",
		"desc": "Sağa sola koşup duran düşman. Durduğu anı yakala, kafayı tek mermiyle al. Gecikirsen o seni vurur.",
		"timed": true},
	{"id": "counter", "name": "Karşı-Strafe", "icon": "⏹",
		"desc": "Koşarken dur ve vur. Sadece durduğunda (hızın %25'in altında) mermi isabetli gider; ne kadar hızlı durduğunu ms olarak gösterir.",
		"timed": true},
	{"id": "placement", "name": "Nişan Yüksekliği", "icon": "—",
		"desc": "Düşman köşelerden çıkar. Çıktığı anda nişangahın kafasına ne kadar uzaktı ölçülür. Nişanı hep kafa hizasında tutmayı öğretir.",
		"timed": false, "rounds": 20},
	{"id": "free", "name": "Serbest Poligon", "icon": "◇",
		"desc": "10, 20 ve 30 metrede duran botlar. Hareket et, tap at, spreyi dene; süre yok. Bitirmek için Esc.",
		"timed": false},
]

static func get_drill(id: String) -> Dictionary:
	for d in LIST:
		if d["id"] == id:
			return d
	return LIST[0]
