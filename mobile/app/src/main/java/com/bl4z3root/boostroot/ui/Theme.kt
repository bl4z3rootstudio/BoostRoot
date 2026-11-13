package com.bl4z3root.boostroot.ui

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** Same palette as the PC app. */
object Palette {
    val Bg = Color(0xFF0B0912)
    val Surface = Color(0xFF15121F)
    val Surface2 = Color(0xFF1D1930)
    val Border = Color(0xFF2A2440)
    val Text = Color(0xFFF2EEFF)
    val Muted = Color(0xFF9C95B5)
    val Good = Color(0xFF34D399)
    val Warn = Color(0xFFFBBF24)
    val Bad = Color(0xFFF87171)

    /** (accent, light) pairs; index stored in prefs. */
    val accents = listOf(
        Color(0xFFA855F7) to Color(0xFFC084FC),
        Color(0xFF3B82F6) to Color(0xFF60A5FA),
        Color(0xFF22C55E) to Color(0xFF4ADE80),
        Color(0xFFEF4444) to Color(0xFFF87171),
        Color(0xFFF97316) to Color(0xFFFB923C),
        Color(0xFFEC4899) to Color(0xFFF472B6),
    )
}

@Composable
fun BoostTheme(accent: Int, content: @Composable () -> Unit) {
    val (a, light) = Palette.accents.getOrElse(accent) { Palette.accents[0] }
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = a, onPrimary = Color.White, secondary = light, background = Palette.Bg, surface = Palette.Surface,
            surfaceVariant = Palette.Surface2, onBackground = Palette.Text, onSurface = Palette.Text, onSurfaceVariant = Palette.Muted,
            outline = Palette.Border, surfaceContainer = Palette.Surface, surfaceContainerHigh = Palette.Surface2,
        ),
    ) {
        // Plain Text() defaults to this colour; without it, text on our dark cards came out nearly black.
        androidx.compose.runtime.CompositionLocalProvider(androidx.compose.material3.LocalContentColor provides Palette.Text, content = content)
    }
}

@Composable
fun Card(modifier: Modifier = Modifier, glow: Boolean = false, content: @Composable ColumnScope.() -> Unit) {
    val accent = MaterialTheme.colorScheme.primary
    Column(
        modifier
            .fillMaxWidth()
            .background(Palette.Surface, RoundedCornerShape(18.dp))
            .border(1.dp, if (glow) accent.copy(alpha = 0.6f) else Palette.Border, RoundedCornerShape(18.dp))
            .padding(16.dp),
        content = content,
    )
}

@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier) =
    Text(text, modifier.padding(top = 8.dp, bottom = 6.dp), fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Palette.Text)

@Composable
fun Muted(text: String, modifier: Modifier = Modifier, size: Int = 13) =
    Text(text, modifier, fontSize = size.sp, color = Palette.Muted, lineHeight = (size + 5).sp)

/** Gently pulsing scale, used on the big buttons (same feel as the PC Boost orb). */
@Composable
fun Modifier.pulse(enabled: Boolean = true): Modifier {
    if (!enabled) return this
    val t = rememberInfiniteTransition(label = "pulse")
    val s by t.animateFloat(1f, 1.035f, infiniteRepeatable(tween(1100, easing = FastOutSlowInEasing), RepeatMode.Reverse), label = "s")
    return this.graphicsLayer { scaleX = s; scaleY = s }
}

fun accentBrush(a: Color, b: Color) = Brush.linearGradient(listOf(a, b))

/** Tiny hand-made icons so we don't ship the 30 MB extended icon set. */
object Glyphs {
    val Crosshair: ImageVector = ImageVector.Builder("crosshair", 24.dp, 24.dp, 24f, 24f).apply {
        path(fill = androidx.compose.ui.graphics.SolidColor(Color.White)) {
            moveTo(11f, 2f); lineTo(13f, 2f); lineTo(13f, 9f); lineTo(11f, 9f); close()
            moveTo(11f, 15f); lineTo(13f, 15f); lineTo(13f, 22f); lineTo(11f, 22f); close()
            moveTo(2f, 11f); lineTo(9f, 11f); lineTo(9f, 13f); lineTo(2f, 13f); close()
            moveTo(15f, 11f); lineTo(22f, 11f); lineTo(22f, 13f); lineTo(15f, 13f); close()
            moveTo(11f, 11f); lineTo(13f, 11f); lineTo(13f, 13f); lineTo(11f, 13f); close()
        }
    }.build()

    val Gamepad: ImageVector = ImageVector.Builder("gamepad", 24.dp, 24.dp, 24f, 24f).apply {
        path(fill = androidx.compose.ui.graphics.SolidColor(Color.White)) {
            moveTo(7f, 6f); lineTo(17f, 6f); curveTo(20f, 6f, 22f, 8.5f, 22f, 12f); lineTo(22f, 15f)
            curveTo(22f, 18f, 19.5f, 19f, 18f, 17.5f); lineTo(15.5f, 15f); lineTo(8.5f, 15f); lineTo(6f, 17.5f)
            curveTo(4.5f, 19f, 2f, 18f, 2f, 15f); lineTo(2f, 12f); curveTo(2f, 8.5f, 4f, 6f, 7f, 6f); close()
            moveTo(6f, 9f); lineTo(6f, 10.5f); lineTo(4.5f, 10.5f); lineTo(4.5f, 12f); lineTo(6f, 12f); lineTo(6f, 13.5f)
            lineTo(7.5f, 13.5f); lineTo(7.5f, 12f); lineTo(9f, 12f); lineTo(9f, 10.5f); lineTo(7.5f, 10.5f); lineTo(7.5f, 9f); close()
        }
    }.build()

    val Signal: ImageVector = ImageVector.Builder("signal", 24.dp, 24.dp, 24f, 24f).apply {
        path(fill = androidx.compose.ui.graphics.SolidColor(Color.White)) {
            moveTo(3f, 17f); lineTo(6f, 17f); lineTo(6f, 21f); lineTo(3f, 21f); close()
            moveTo(8f, 13f); lineTo(11f, 13f); lineTo(11f, 21f); lineTo(8f, 21f); close()
            moveTo(13f, 9f); lineTo(16f, 9f); lineTo(16f, 21f); lineTo(13f, 21f); close()
            moveTo(18f, 4f); lineTo(21f, 4f); lineTo(21f, 21f); lineTo(18f, 21f); close()
        }
    }.build()
}
