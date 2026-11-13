package com.bl4z3root.boostroot.ui

import android.graphics.drawable.Drawable
import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.graphics.drawable.toBitmap
import com.bl4z3root.boostroot.R
import com.bl4z3root.boostroot.core.GameCatalog
import com.bl4z3root.boostroot.core.Tip
import com.bl4z3root.boostroot.core.TipKey
import com.bl4z3root.boostroot.core.TipValue
import com.bl4z3root.boostroot.platform.GameSessionService
import com.bl4z3root.boostroot.platform.Games
import com.bl4z3root.boostroot.platform.InstalledGame
import com.bl4z3root.boostroot.platform.Permissions
import com.bl4z3root.boostroot.platform.Prefs
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
fun GamesScreen(prefs: Prefs) {
    val ctx = LocalContext.current
    var games by remember { mutableStateOf<List<InstalledGame>?>(null) }
    var open by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(Unit) { games = withContext(Dispatchers.IO) { Games.scan(ctx) } }

    LazyColumn(contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            SectionTitle(stringResource(R.string.games_title))
            val g = games
            if (g != null) Muted(if (g.isEmpty()) stringResource(R.string.games_none) else stringResource(R.string.games_found, g.size))
        }
        items(games.orEmpty(), key = { it.packageName }) { game ->
            GameRow(game, expanded = open == game.packageName, onClick = { open = if (open == game.packageName) null else game.packageName }, prefs = prefs)
        }
        item {
            Card {
                Text(stringResource(R.string.general_tips_title), fontWeight = FontWeight.Bold, fontSize = 16.sp)
                Spacer(Modifier.height(6.dp))
                listOf(R.string.gtip_close, R.string.gtip_saver, R.string.gtip_cool, R.string.gtip_wifi).forEach {
                    Row(Modifier.padding(vertical = 3.dp)) { Text("•  ", color = Palette.Muted); Muted(stringResource(it)) }
                }
            }
        }
    }
}

@Composable
private fun GameRow(game: InstalledGame, expanded: Boolean, onClick: () -> Unit, prefs: Prefs) {
    val ctx = LocalContext.current
    Card(Modifier.animateContentSize(), glow = expanded) {
        Row(Modifier.fillMaxWidth().clickable(onClick = onClick), verticalAlignment = Alignment.CenterVertically) {
            AppIcon(game.icon)
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(game.label, fontWeight = FontWeight.SemiBold, fontSize = 16.sp, maxLines = 1)
                if (GameCatalog.find(game.packageName) != null) Muted(stringResource(R.string.game_tips), size = 12)
            }
            OutlinedButton(onClick = { Games.launch(ctx, game.packageName) }) { Text(stringResource(R.string.game_open)) }
        }
        if (expanded) {
            Spacer(Modifier.height(12.dp))
            var dnd by remember { mutableStateOf(prefs.gmDnd) }
            var cross by remember { mutableStateOf(prefs.gmCrosshair) }
            var screen by remember { mutableStateOf(prefs.gmScreen) }
            Toggle(stringResource(R.string.gm_dnd), dnd) { dnd = it; prefs.gmDnd = it }
            Toggle(stringResource(R.string.gm_crosshair), cross) { cross = it; prefs.gmCrosshair = it }
            Toggle(stringResource(R.string.gm_screen), screen) { screen = it; prefs.gmScreen = it }
            if (dnd && !Permissions.hasDnd(ctx)) PermissionRow(R.string.perm_dnd_title, R.string.perm_dnd_desc) { ctx.startActivity(Permissions.dndIntent()) }
            if ((cross || screen) && !Permissions.canOverlay(ctx)) PermissionRow(R.string.perm_overlay_title, R.string.perm_overlay_desc) { ctx.startActivity(Permissions.overlayIntent(ctx)) }
            Spacer(Modifier.height(8.dp))
            Button(
                onClick = {
                    GameSessionService.start(ctx, dnd, cross, screen)
                    Games.launch(ctx, game.packageName)
                },
                modifier = Modifier.fillMaxWidth().height(52.dp).pulse(),
                shape = RoundedCornerShape(14.dp),
            ) { Text(stringResource(R.string.game_start), fontWeight = FontWeight.Bold, fontSize = 16.sp) }

            Spacer(Modifier.height(14.dp))
            Text(stringResource(R.string.game_tips), fontWeight = FontWeight.Bold)
            GameCatalog.tipsFor(game.packageName).forEach { TipLine(it) }
            Spacer(Modifier.height(4.dp))
            Muted(stringResource(R.string.game_tips_note), size = 12)
        }
    }
}

@Composable
fun PermissionRow(title: Int, desc: Int, onGrant: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().padding(vertical = 6.dp).background(Palette.Surface2, RoundedCornerShape(12.dp)).padding(10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            Text(stringResource(title), fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
            Muted(stringResource(desc), size = 12)
        }
        Button(onClick = onGrant, colors = ButtonDefaults.buttonColors()) { Text(stringResource(R.string.perm_grant)) }
    }
}

@Composable
private fun TipLine(tip: Tip) {
    Row(Modifier.fillMaxWidth().padding(vertical = 3.dp), horizontalArrangement = Arrangement.SpaceBetween) {
        Muted(stringResource(tipLabel(tip.key)))
        Text(tipValue(tip.value), fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
    }
}

private fun tipLabel(k: TipKey) = when (k) {
    TipKey.GRAPHICS -> R.string.tipkey_graphics
    TipKey.FRAME_RATE -> R.string.tipkey_frame_rate
    TipKey.SHADOWS -> R.string.tipkey_shadows
    TipKey.ANTI_ALIASING -> R.string.tipkey_anti_aliasing
    TipKey.AUTO_ADJUST -> R.string.tipkey_auto_adjust
    TipKey.STYLE -> R.string.tipkey_style
    TipKey.RESOLUTION -> R.string.tipkey_resolution
    TipKey.EFFECTS -> R.string.tipkey_effects
    TipKey.RENDER_DISTANCE -> R.string.tipkey_render_distance
    TipKey.QUALITY -> R.string.tipkey_quality
    TipKey.BATTERY_SAVER -> R.string.tipkey_battery_saver
    TipKey.HIGH_FPS_MODE -> R.string.tipkey_high_fps
}

@Composable
private fun tipValue(v: TipValue): String = when (v) {
    is TipValue.Lit -> v.text
    TipValue.Word.OFF -> stringResource(R.string.word_off)
    TipValue.Word.ON -> stringResource(R.string.word_on)
    TipValue.Word.LOW -> stringResource(R.string.word_low)
    TipValue.Word.MEDIUM -> stringResource(R.string.word_medium)
    TipValue.Word.HIGH -> stringResource(R.string.word_high)
    TipValue.Word.MAX -> stringResource(R.string.word_max)
    TipValue.Word.LOWEST -> stringResource(R.string.word_lowest)
}

@Composable
private fun AppIcon(d: Drawable?) {
    val bmp = remember(d) { d?.let { runCatching { it.toBitmap(96, 96).asImageBitmap() }.getOrNull() } }
    Box(Modifier.size(48.dp).clip(RoundedCornerShape(12.dp)).background(Palette.Surface2), contentAlignment = Alignment.Center) {
        if (bmp != null) Image(bmp, null, Modifier.size(48.dp))
    }
}
