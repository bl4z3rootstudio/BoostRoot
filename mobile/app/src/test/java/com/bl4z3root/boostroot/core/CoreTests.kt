package com.bl4z3root.boostroot.core

import org.junit.Assert.*
import org.junit.Test

class CrosshairTests {
    @Test fun valorantRoundTrip() {
        val d = CrosshairDesign(color = "#FF00FF", dot = true, dotThickness = 3, inner = CrosshairLines(true, 3, 8, 2, 1.0))
        val back = CrosshairDesign.fromValorantCode(d.toValorantCode())!!
        assertEquals("#FF00FF", back.color)
        assertTrue(back.dot); assertEquals(3, back.dotThickness)
        assertEquals(3, back.inner.thickness); assertEquals(8, back.inner.length); assertEquals(2, back.inner.offset)
        assertFalse(back.outer.show)
    }

    @Test fun customColourUsesU() {
        val code = CrosshairDesign(color = "#12AB34").toValorantCode()
        assertTrue(code.contains(";c;8;u;12AB34FF"))
        assertEquals("#12AB34", CrosshairDesign.fromValorantCode(code)!!.color)
    }

    @Test fun pcAppCodeIsReadable() {
        // Same format the PC app writes (shared codes between phone and PC).
        val d = CrosshairDesign.fromValorantCode("0;P;c;5;h;0;0l;4;0o;2;0a;1;1b;0")!!
        assertEquals("#00FFFF", d.color); assertFalse(d.outline); assertEquals(4, d.inner.length)
    }

    @Test fun badCodes() {
        assertNull(CrosshairDesign.fromValorantCode("hello"))
        assertNull(CrosshairDesign.fromValorantCode(""))
    }

    @Test fun serializeRoundTrip() {
        val d = CrosshairDesign.PREMIUM_PRESETS[1].copy(scale = 1.5f, offsetX = 3, offsetY = -2)
        assertEquals(d, CrosshairDesign.deserialize(CrosshairDesign.serialize(d)))
        assertNull(CrosshairDesign.deserialize("broken"))
    }

    @Test fun colourParse() {
        assertEquals(0xFFFF0000.toInt(), CrosshairDesign.parseColor("#FF0000"))
        assertEquals(0xFF00FF66.toInt(), CrosshairDesign.parseColor("nonsense"))
    }
}

class CatalogTests {
    @Test fun knownAndUnknown() {
        assertEquals("PUBG MOBILE", GameCatalog.find("com.pubg.imobile")!!.name)
        assertEquals(GameCatalog.genericTips, GameCatalog.tipsFor("com.example.unknown"))
        val pkgs = GameCatalog.games.flatMap { it.packages }
        assertEquals(pkgs.size, pkgs.toSet().size)
    }
}

class PingTests {
    @Test fun statsAndQuality() {
        val s = PingStats(10)
        listOf(20.0, 22.0, 21.0, 23.0).forEach(s::add)
        assertEquals(0, s.quality); assertEquals(0.0, s.lossPercent, 0.0)
        s.add(null)
        assertEquals(20.0, s.lossPercent, 0.001); assertEquals(2, s.quality)
        repeat(20) { s.add(30.0) }
        assertEquals(10, s.values.size); assertEquals(0, s.quality)
    }
}
