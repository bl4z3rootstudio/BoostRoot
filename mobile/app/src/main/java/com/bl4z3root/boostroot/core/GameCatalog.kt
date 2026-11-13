package com.bl4z3root.boostroot.core

/** Setting names shown in tips; the UI translates them. */
enum class TipKey { GRAPHICS, FRAME_RATE, SHADOWS, ANTI_ALIASING, AUTO_ADJUST, STYLE, RESOLUTION, EFFECTS, RENDER_DISTANCE, QUALITY, BATTERY_SAVER, HIGH_FPS_MODE }

/** Translated values. [Lit] is a literal shown as in the game's own menu (e.g. "Smooth", "Ultra"). */
sealed interface TipValue {
    enum class Word : TipValue { OFF, ON, LOW, MEDIUM, HIGH, MAX, LOWEST }
    data class Lit(val text: String) : TipValue
}

data class Tip(val key: TipKey, val value: TipValue)

enum class Genre { SHOOTER, BATTLE_ROYALE, MOBA, SPORTS, RACING, STRATEGY, OPEN_WORLD, SANDBOX, CASUAL, OTHER }

data class KnownGame(val packages: List<String>, val name: String, val genre: Genre, val tips: List<Tip>)

object GameCatalog {
    private fun lit(s: String) = TipValue.Lit(s)
    private val OFF = TipValue.Word.OFF
    private val ON = TipValue.Word.ON
    private val LOW = TipValue.Word.LOW
    private val MED = TipValue.Word.MEDIUM
    private val MAX = TipValue.Word.MAX

    val games: List<KnownGame> = listOf(
        KnownGame(listOf("com.tencent.ig", "com.pubg.imobile", "com.pubg.krmobile", "com.vng.pubgmobile", "com.rekoo.pubgm"), "PUBG MOBILE", Genre.BATTLE_ROYALE, listOf(
            Tip(TipKey.GRAPHICS, lit("Smooth")), Tip(TipKey.FRAME_RATE, lit("Extreme / 90 FPS / 120 FPS")),
            Tip(TipKey.STYLE, lit("Colorful")), Tip(TipKey.ANTI_ALIASING, OFF), Tip(TipKey.SHADOWS, OFF), Tip(TipKey.AUTO_ADJUST, OFF))),
        KnownGame(listOf("com.dts.freefireth", "com.dts.freefiremax"), "Free Fire", Genre.BATTLE_ROYALE, listOf(
            Tip(TipKey.GRAPHICS, lit("Smooth")), Tip(TipKey.HIGH_FPS_MODE, ON), Tip(TipKey.SHADOWS, OFF))),
        KnownGame(listOf("com.activision.callofduty.shooter"), "Call of Duty: Mobile", Genre.SHOOTER, listOf(
            Tip(TipKey.GRAPHICS, LOW), Tip(TipKey.FRAME_RATE, MAX), Tip(TipKey.SHADOWS, OFF), Tip(TipKey.ANTI_ALIASING, OFF),
            Tip(TipKey.EFFECTS, lit("Ragdoll / Depth of field: Off")))),
        KnownGame(listOf("com.axlebolt.standoff2"), "Standoff 2", Genre.SHOOTER, listOf(
            Tip(TipKey.GRAPHICS, LOW), Tip(TipKey.FRAME_RATE, MAX), Tip(TipKey.SHADOWS, OFF), Tip(TipKey.RESOLUTION, MED))),
        KnownGame(listOf("com.epicgames.fortnite"), "Fortnite", Genre.BATTLE_ROYALE, listOf(
            Tip(TipKey.QUALITY, lit("Medium / Performance")), Tip(TipKey.FRAME_RATE, MAX), Tip(TipKey.SHADOWS, OFF), Tip(TipKey.EFFECTS, LOW))),
        KnownGame(listOf("com.mobile.legends"), "Mobile Legends: Bang Bang", Genre.MOBA, listOf(
            Tip(TipKey.HIGH_FPS_MODE, ON), Tip(TipKey.GRAPHICS, MED), Tip(TipKey.SHADOWS, OFF), Tip(TipKey.EFFECTS, lit("HD mode: Off")))),
        KnownGame(listOf("com.riotgames.league.wildrift"), "League of Legends: Wild Rift", Genre.MOBA, listOf(
            Tip(TipKey.FRAME_RATE, MAX), Tip(TipKey.GRAPHICS, MED), Tip(TipKey.SHADOWS, OFF), Tip(TipKey.EFFECTS, LOW))),
        KnownGame(listOf("com.supercell.brawlstars"), "Brawl Stars", Genre.CASUAL, listOf(
            Tip(TipKey.FRAME_RATE, lit("120 FPS / 60 FPS")), Tip(TipKey.QUALITY, MED), Tip(TipKey.BATTERY_SAVER, OFF))),
        KnownGame(listOf("com.supercell.clashroyale"), "Clash Royale", Genre.STRATEGY, listOf(
            Tip(TipKey.BATTERY_SAVER, OFF), Tip(TipKey.FRAME_RATE, MAX))),
        KnownGame(listOf("com.supercell.clashofclans"), "Clash of Clans", Genre.STRATEGY, listOf(
            Tip(TipKey.FRAME_RATE, MAX), Tip(TipKey.BATTERY_SAVER, OFF))),
        KnownGame(listOf("com.miHoYo.GenshinImpact"), "Genshin Impact", Genre.OPEN_WORLD, listOf(
            Tip(TipKey.FRAME_RATE, lit("60")), Tip(TipKey.QUALITY, LOW), Tip(TipKey.SHADOWS, LOW), Tip(TipKey.EFFECTS, LOW),
            Tip(TipKey.ANTI_ALIASING, OFF), Tip(TipKey.RESOLUTION, MED))),
        KnownGame(listOf("com.HoYoverse.hkrpgoversea"), "Honkai: Star Rail", Genre.OPEN_WORLD, listOf(
            Tip(TipKey.FRAME_RATE, lit("60")), Tip(TipKey.QUALITY, MED), Tip(TipKey.SHADOWS, LOW), Tip(TipKey.ANTI_ALIASING, OFF))),
        KnownGame(listOf("com.HoYoverse.Nap"), "Zenless Zone Zero", Genre.OPEN_WORLD, listOf(
            Tip(TipKey.FRAME_RATE, lit("60")), Tip(TipKey.QUALITY, LOW), Tip(TipKey.SHADOWS, LOW))),
        KnownGame(listOf("com.roblox.client"), "Roblox", Genre.SANDBOX, listOf(
            Tip(TipKey.GRAPHICS, lit("Manual: 3–5")), Tip(TipKey.FRAME_RATE, lit("Maximum Frame Rate: 60+")))),
        KnownGame(listOf("com.mojang.minecraftpe"), "Minecraft", Genre.SANDBOX, listOf(
            Tip(TipKey.RENDER_DISTANCE, lit("8–12 chunks")), Tip(TipKey.GRAPHICS, lit("Fancy Graphics: Off")),
            Tip(TipKey.EFFECTS, lit("Beautiful Skies / Smooth Lighting: Off")))),
        KnownGame(listOf("com.ea.gp.fifamobile"), "EA SPORTS FC Mobile", Genre.SPORTS, listOf(
            Tip(TipKey.FRAME_RATE, lit("High / 60")), Tip(TipKey.GRAPHICS, MED))),
        KnownGame(listOf("jp.konami.pesam"), "eFootball", Genre.SPORTS, listOf(
            Tip(TipKey.FRAME_RATE, lit("High")), Tip(TipKey.QUALITY, MED))),
        KnownGame(listOf("com.gameloft.android.ANMP.GloftA9HM"), "Asphalt Legends", Genre.RACING, listOf(
            Tip(TipKey.QUALITY, lit("Performance")), Tip(TipKey.FRAME_RATE, MAX), Tip(TipKey.EFFECTS, LOW))),
        KnownGame(listOf("com.miniclip.eightballpool"), "8 Ball Pool", Genre.CASUAL, listOf(Tip(TipKey.BATTERY_SAVER, OFF))),
        KnownGame(listOf("com.innersloth.spacemafia"), "Among Us", Genre.CASUAL, listOf(Tip(TipKey.FRAME_RATE, MAX))),
        KnownGame(listOf("com.kiloo.subwaysurf"), "Subway Surfers", Genre.CASUAL, listOf(Tip(TipKey.BATTERY_SAVER, OFF))),
        KnownGame(listOf("com.nianticlabs.pokemongo"), "Pokémon GO", Genre.OPEN_WORLD, listOf(
            Tip(TipKey.BATTERY_SAVER, OFF), Tip(TipKey.FRAME_RATE, lit("Native refresh rate: On")))),
    )

    private val byPackage: Map<String, KnownGame> = games.flatMap { g -> g.packages.map { it to g } }.toMap()

    fun find(packageName: String): KnownGame? = byPackage[packageName]

    /** Tips for games we don't know yet: safe advice that fits any game. */
    val genericTips: List<Tip> = listOf(
        Tip(TipKey.FRAME_RATE, MAX), Tip(TipKey.SHADOWS, OFF), Tip(TipKey.ANTI_ALIASING, OFF), Tip(TipKey.BATTERY_SAVER, OFF),
    )

    fun tipsFor(packageName: String): List<Tip> = find(packageName)?.tips ?: genericTips
}
