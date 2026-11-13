package com.bl4z3root.boostroot.core

import java.util.Locale

/** One set of the four crosshair arms (same model as the PC app, so codes are interchangeable). */
data class CrosshairLines(
    val show: Boolean = true,
    val thickness: Int = 2,
    val length: Int = 6,
    val offset: Int = 3,
    val opacity: Double = 1.0,
)

data class CrosshairDesign(
    val name: String = "BoostRoot",
    val color: String = "#00FF66",
    val outline: Boolean = true,
    val outlineThickness: Int = 1,
    val outlineOpacity: Double = 0.5,
    val dot: Boolean = false,
    val dotThickness: Int = 2,
    val dotOpacity: Double = 1.0,
    val inner: CrosshairLines = CrosshairLines(),
    val outer: CrosshairLines = CrosshairLines(show = false, thickness = 2, length = 2, offset = 10, opacity = 0.35),
    val hideTop: Boolean = false,
    /** On-screen size multiplier for the phone overlay (1 = 1 crosshair unit ≈ 1.5 dp). */
    val scale: Float = 1f,
    /** Overlay nudge from the exact screen centre, in dp (some games aim slightly off-centre). */
    val offsetX: Int = 0,
    val offsetY: Int = 0,
) {
    val argb: Int get() = parseColor(color)

    fun toValorantCode(): String {
        val sb = StringBuilder("0;P")
        fun add(k: String, v: String) { sb.append(';').append(k).append(';').append(v) }
        val hex = color.trimStart('#').uppercase(Locale.ROOT)
        val idx = VALORANT_COLORS.indexOfFirst { it == hex }
        if (idx >= 0) { if (idx != 0) add("c", idx.toString()) } else { add("c", "8"); add("u", hex + "FF") }
        if (!outline) add("h", "0") else {
            if (outlineThickness != 1) add("t", outlineThickness.coerceIn(1, 6).toString())
            if (kotlin.math.abs(outlineOpacity - 0.5) > 0.001) add("o", fmt(outlineOpacity))
        }
        if (dot) {
            add("d", "1")
            if (dotThickness != 2) add("z", dotThickness.coerceIn(1, 6).toString())
            if (kotlin.math.abs(dotOpacity - 1) > 0.001) add("a", fmt(dotOpacity))
        }
        fun lines(p: String, l: CrosshairLines, def: CrosshairLines) {
            if (!l.show) { add(p + "b", "0"); return }
            if (l.thickness != def.thickness) add(p + "t", l.thickness.coerceIn(0, 10).toString())
            if (l.length != def.length) add(p + "l", l.length.coerceIn(0, 20).toString())
            if (l.offset != def.offset) add(p + "o", l.offset.coerceIn(0, 20).toString())
            if (kotlin.math.abs(l.opacity - def.opacity) > 0.001) add(p + "a", fmt(l.opacity))
        }
        lines("0", inner, CrosshairLines(true, 2, 6, 3, 0.8))
        if (outer.show) lines("1", outer, CrosshairLines(true, 2, 2, 10, 0.35)) else add("1b", "0")
        return sb.toString()
    }

    companion object {
        /** VALORANT colour index → hex (index 8 = custom "u" value). */
        private val VALORANT_COLORS = listOf("FFFFFF", "00FF00", "7FFF00", "DFFF00", "FFFF00", "00FFFF", "FF00FF", "FF0000")

        val PRESETS: List<CrosshairDesign> = listOf(
            CrosshairDesign("Classic", "#00FF66", inner = CrosshairLines(true, 2, 6, 3, 1.0)),
            CrosshairDesign("Thin plus", "#00FFFF", outlineOpacity = 1.0, inner = CrosshairLines(true, 1, 4, 2, 1.0)),
            CrosshairDesign("Dot", "#FFFFFF", dot = true, dotThickness = 3, inner = CrosshairLines(false)),
            CrosshairDesign("Short bold", "#FFFF00", inner = CrosshairLines(true, 3, 3, 2, 1.0)),
            CrosshairDesign("Pink dot + plus", "#FF5CF0", dot = true, inner = CrosshairLines(true, 2, 4, 3, 0.8)),
            CrosshairDesign("Red T", "#FF3B3B", hideTop = true, inner = CrosshairLines(true, 2, 5, 3, 1.0)),
        )

        /** Extra presets unlocked by watching a rewarded ad. */
        val PREMIUM_PRESETS: List<CrosshairDesign> = listOf(
            CrosshairDesign("Wide gap", "#00FF66", inner = CrosshairLines(true, 2, 6, 8, 1.0), outer = CrosshairLines(true, 2, 3, 16, 0.5)),
            CrosshairDesign("Sniper", "#FF3B3B", dot = true, dotThickness = 2, inner = CrosshairLines(true, 1, 14, 6, 0.9)),
            CrosshairDesign("Circle dot", "#00FFFF", dot = true, dotThickness = 4, outline = true, outlineThickness = 2, inner = CrosshairLines(false)),
            CrosshairDesign("Neon", "#B44CFF", dot = true, inner = CrosshairLines(true, 2, 5, 4, 1.0), outer = CrosshairLines(true, 1, 2, 12, 0.6)),
            CrosshairDesign("Tiny", "#FFFFFF", outline = false, inner = CrosshairLines(true, 1, 2, 1, 1.0)),
            CrosshairDesign("Gold", "#FFC83D", dot = true, inner = CrosshairLines(true, 2, 4, 2, 1.0)),
        )

        fun fromValorantCode(code: String): CrosshairDesign? {
            val parts = code.trim().split(';')
            if (parts.size < 2 || parts[0] != "0") return null
            val d = HashMap<String, String>()
            var section = ""
            var i = 1
            while (i < parts.size) {
                val p = parts[i]
                if (p == "P" || p == "A" || p == "S") { section = p; i++; continue }
                if (i + 1 >= parts.size) break
                if (section == "P") d[p] = parts[i + 1]
                i += 2
            }
            fun int(k: String, def: Int) = d[k]?.toIntOrNull() ?: def
            fun dbl(k: String, def: Double) = d[k]?.toDoubleOrNull() ?: def
            val c = int("c", 0)
            val u = d["u"]
            val color = if (c == 8 && u != null && u.length >= 6) "#" + u.substring(0, 6).uppercase(Locale.ROOT)
            else "#" + VALORANT_COLORS.getOrElse(c) { VALORANT_COLORS[0] }
            return CrosshairDesign(
                name = "VALORANT", color = color,
                outline = int("h", 1) == 1, outlineThickness = int("t", 1), outlineOpacity = dbl("o", 0.5),
                dot = int("d", 0) == 1, dotThickness = int("z", 2), dotOpacity = dbl("a", 1.0),
                inner = CrosshairLines(int("0b", 1) == 1, int("0t", 2), int("0l", 6), int("0o", 3), dbl("0a", 0.8)),
                outer = CrosshairLines(int("1b", 1) == 1, int("1t", 2), int("1l", 2), int("1o", 10), dbl("1a", 0.35)),
            )
        }

        fun parseColor(hex: String): Int {
            val h = hex.trimStart('#')
            val v = if (h.length == 6) h.toLongOrNull(16) else null
            return ((0xFF000000L) or (v ?: 0x00FF66L)).toInt()
        }

        private fun fmt(v: Double): String {
            val s = String.format(Locale.ROOT, "%.3f", v).trimEnd('0').trimEnd('.')
            return if (s.isEmpty()) "0" else s
        }

        // --- tiny, dependency-free persistence (one line per design) ---
        fun serialize(d: CrosshairDesign): String = listOf(
            d.name.replace('|', '/'), d.color, d.outline, d.outlineThickness, d.outlineOpacity, d.dot, d.dotThickness, d.dotOpacity,
            d.inner.show, d.inner.thickness, d.inner.length, d.inner.offset, d.inner.opacity,
            d.outer.show, d.outer.thickness, d.outer.length, d.outer.offset, d.outer.opacity,
            d.hideTop, d.scale, d.offsetX, d.offsetY,
        ).joinToString("|")

        fun deserialize(s: String): CrosshairDesign? = runCatching {
            val p = s.split('|')
            fun b(i: Int) = p[i].toBooleanStrict()
            fun n(i: Int) = p[i].toInt()
            fun f(i: Int) = p[i].toDouble()
            CrosshairDesign(
                p[0], p[1], b(2), n(3), f(4), b(5), n(6), f(7),
                CrosshairLines(b(8), n(9), n(10), n(11), f(12)),
                CrosshairLines(b(13), n(14), n(15), n(16), f(17)),
                b(18), p[19].toFloat(), n(20), n(21),
            )
        }.getOrNull()
    }
}
