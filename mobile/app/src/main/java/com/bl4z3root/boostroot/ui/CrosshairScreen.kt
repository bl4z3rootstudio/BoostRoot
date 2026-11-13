package com.bl4z3root.boostroot.ui

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.widget.Toast
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.bl4z3root.boostroot.R
import com.bl4z3root.boostroot.ads.Ads
import com.bl4z3root.boostroot.core.CrosshairDesign
import com.bl4z3root.boostroot.platform.CrosshairPainter
import com.bl4z3root.boostroot.platform.GameSessionService
import com.bl4z3root.boostroot.platform.Permissions
import com.bl4z3root.boostroot.platform.Prefs
import kotlin.math.roundToInt

private val SWATCHES = listOf("#00FF66", "#00FFFF", "#FFFFFF", "#FFFF00", "#FF3B3B", "#FF5CF0", "#B44CFF", "#FFC83D")

@Composable
fun CrosshairScreen(prefs: Prefs) {
    val ctx = LocalContext.current
    var d by remember { mutableStateOf(prefs.crosshair) }
    var premium by remember { mutableStateOf(prefs.premium) }
    val session by GameSessionService.running.collectAsState()
    fun set(n: CrosshairDesign) { d = n; prefs.crosshair = n }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Preview(d)

        Card {
            val on = session?.crosshair == true
            Toggle(stringResource(R.string.crosshair_show), on) { want ->
                if (want && !Permissions.canOverlay(ctx)) {
                    Toast.makeText(ctx, R.string.crosshair_need_overlay, Toast.LENGTH_SHORT).show()
                    ctx.startActivity(Permissions.overlayIntent(ctx))
                } else if (want) GameSessionService.start(ctx, dnd = false, crosshair = true, keepOn = false)
                else GameSessionService.stop(ctx)
            }
            if (!Permissions.canOverlay(ctx)) PermissionRow(R.string.perm_overlay_title, R.string.perm_overlay_desc) { ctx.startActivity(Permissions.overlayIntent(ctx)) }
        }

        SectionTitle(stringResource(R.string.crosshair_presets))
        PresetRow(CrosshairDesign.PRESETS, locked = false) { set(it.copy(scale = d.scale, offsetX = d.offsetX, offsetY = d.offsetY)) }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.crosshair_premium), fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            if (premium) Muted(stringResource(R.string.crosshair_unlocked), size = 12)
        }
        PresetRow(CrosshairDesign.PREMIUM_PRESETS, locked = !premium) { set(it.copy(scale = d.scale, offsetX = d.offsetX, offsetY = d.offsetY)) }
        if (!premium) {
            OutlinedButton(onClick = {
                val act = ctx as? Activity ?: return@OutlinedButton
                Ads.showRewarded(act) {
                    prefs.premiumUntil = System.currentTimeMillis() + 24 * 3600 * 1000L
                    premium = true
                }
            }, modifier = Modifier.fillMaxWidth()) { Text(stringResource(R.string.crosshair_unlock)) }
        }

        Card {
            Text(stringResource(R.string.crosshair_color), fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                SWATCHES.forEach { hex ->
                    val c = Color(CrosshairDesign.parseColor(hex))
                    Box(
                        Modifier.size(30.dp).background(c, CircleShape)
                            .border(if (d.color.equals(hex, true)) 3.dp else 0.dp, Color.White, CircleShape)
                            .clickable { set(d.copy(color = hex)) },
                    )
                }
            }
            Spacer(Modifier.height(6.dp))
            IntSlider(R.string.crosshair_size, (d.scale * 10).roundToInt(), 5..30) { set(d.copy(scale = it / 10f)) }
            IntSlider(R.string.crosshair_thickness, d.inner.thickness, 1..8) { set(d.copy(inner = d.inner.copy(thickness = it))) }
            IntSlider(R.string.crosshair_length, d.inner.length, 0..20) { set(d.copy(inner = d.inner.copy(length = it, show = it > 0))) }
            IntSlider(R.string.crosshair_gap, d.inner.offset, 0..20) { set(d.copy(inner = d.inner.copy(offset = it))) }
            IntSlider(R.string.crosshair_opacity, (d.inner.opacity * 100).roundToInt(), 10..100) { set(d.copy(inner = d.inner.copy(opacity = it / 100.0))) }
            Toggle(stringResource(R.string.crosshair_dot), d.dot) { set(d.copy(dot = it)) }
            Toggle(stringResource(R.string.crosshair_outline), d.outline) { set(d.copy(outline = it)) }
            Toggle(stringResource(R.string.crosshair_hide_top), d.hideTop) { set(d.copy(hideTop = it)) }
            IntSlider(R.string.crosshair_offset_x, d.offsetX, -60..60) { set(d.copy(offsetX = it)) }
            IntSlider(R.string.crosshair_offset_y, d.offsetY, -60..60) { set(d.copy(offsetY = it)) }
            if (session?.crosshair == true) Muted("↻", size = 11)
        }

        Card {
            Text(stringResource(R.string.crosshair_code), fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.height(4.dp))
            Muted(d.toValorantCode(), size = 12)
            Spacer(Modifier.height(8.dp))
            val cm = ctx.getSystemService(ClipboardManager::class.java)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = {
                    cm.setPrimaryClip(ClipData.newPlainText("crosshair", d.toValorantCode()))
                    Toast.makeText(ctx, R.string.crosshair_copied, Toast.LENGTH_SHORT).show()
                }) { Text(stringResource(R.string.crosshair_copy)) }
                OutlinedButton(onClick = {
                    val text = cm.primaryClip?.getItemAt(0)?.coerceToText(ctx)?.toString().orEmpty()
                    val imported = CrosshairDesign.fromValorantCode(text)
                    if (imported == null) Toast.makeText(ctx, R.string.crosshair_bad_code, Toast.LENGTH_SHORT).show()
                    else { set(imported.copy(scale = d.scale)); Toast.makeText(ctx, R.string.crosshair_imported, Toast.LENGTH_SHORT).show() }
                }) { Text(stringResource(R.string.crosshair_import)) }
            }
        }
        // A running overlay picks up the new design when the session restarts.
        if (session?.crosshair == true) androidx.compose.runtime.LaunchedEffect(d) {
            GameSessionService.start(ctx, session!!.dnd, true, session!!.keepOn)
        }
    }
}

@Composable
private fun Preview(d: CrosshairDesign) {
    val unit = with(LocalDensity.current) { 1.5.dp.toPx() }
    val a = MaterialTheme.colorScheme.primary
    Box(
        Modifier.fillMaxWidth().height(200.dp)
            .background(Brush.linearGradient(listOf(Color(0xFF3B4A5C), Color(0xFF1C2430), a.copy(alpha = 0.35f))), RoundedCornerShape(18.dp)),
    ) {
        Canvas(Modifier.fillMaxSize()) {
            drawIntoCanvas { CrosshairPainter.draw(it.nativeCanvas, size.width / 2 + d.offsetX * unit / 1.5f, size.height / 2 + d.offsetY * unit / 1.5f, d, unit) }
        }
    }
}

@Composable
private fun PresetRow(list: List<CrosshairDesign>, locked: Boolean, onPick: (CrosshairDesign) -> Unit) {
    val unit = with(LocalDensity.current) { 1.2.dp.toPx() }
    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        items(list) { p ->
            Box(
                Modifier.size(72.dp).background(Palette.Surface2, RoundedCornerShape(14.dp))
                    .border(1.dp, Palette.Border, RoundedCornerShape(14.dp))
                    .clickable(enabled = !locked) { onPick(p) },
                contentAlignment = Alignment.Center,
            ) {
                Canvas(Modifier.fillMaxSize()) { drawIntoCanvas { CrosshairPainter.draw(it.nativeCanvas, size.width / 2, size.height / 2, p, unit) } }
                if (locked) Box(Modifier.fillMaxSize().background(Color(0x99000000), RoundedCornerShape(14.dp)), contentAlignment = Alignment.Center) {
                    Text("🔒", fontSize = 18.sp)
                }
            }
        }
    }
}

@Composable
private fun IntSlider(label: Int, value: Int, range: IntRange, onChange: (Int) -> Unit) {
    Column(Modifier.padding(top = 4.dp)) {
        Row {
            Text(stringResource(label), Modifier.weight(1f), fontSize = 14.sp)
            Text(value.toString(), color = Palette.Muted, fontSize = 13.sp)
        }
        Slider(
            value = value.toFloat(), onValueChange = { onChange(it.roundToInt()) },
            valueRange = range.first.toFloat()..range.last.toFloat(),
        )
    }
}
