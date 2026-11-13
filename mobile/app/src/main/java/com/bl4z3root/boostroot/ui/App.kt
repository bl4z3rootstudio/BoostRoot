package com.bl4z3root.boostroot.ui

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.bl4z3root.boostroot.R
import com.bl4z3root.boostroot.ads.Ads
import com.bl4z3root.boostroot.ads.BannerAd
import com.bl4z3root.boostroot.platform.GameSessionService
import com.bl4z3root.boostroot.platform.Prefs

private data class Tab(val title: Int, val icon: ImageVector)

@Composable
fun App(prefs: Prefs, onAccent: (Int) -> Unit) {
    val tabs = listOf(
        Tab(R.string.tab_games, Glyphs.Gamepad), Tab(R.string.tab_crosshair, Glyphs.Crosshair),
        Tab(R.string.tab_network, Glyphs.Signal), Tab(R.string.tab_settings, Icons.Filled.Settings),
    )
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val session by GameSessionService.running.collectAsState()
    val adsReady by Ads.ready.collectAsState()
    val ctx = LocalContext.current

    Scaffold(
        containerColor = Palette.Bg,
        bottomBar = {
            Column {
                if (adsReady) BannerAd()
                NavigationBar(containerColor = Palette.Surface) {
                    tabs.forEachIndexed { i, t ->
                        NavigationBarItem(
                            selected = tab == i, onClick = { tab = i },
                            icon = { Icon(t.icon, null, Modifier.size(22.dp)) },
                            label = { Text(stringResource(t.title), fontSize = 11.sp, maxLines = 1) },
                            colors = NavigationBarItemDefaults.colors(
                                selectedIconColor = MaterialTheme.colorScheme.secondary, selectedTextColor = MaterialTheme.colorScheme.secondary,
                                indicatorColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.18f),
                                unselectedIconColor = Palette.Muted, unselectedTextColor = Palette.Muted,
                            ),
                        )
                    }
                }
            }
        },
    ) { pad ->
        Column(Modifier.fillMaxSize().padding(pad).statusBarsPadding()) {
            Header()
            AnimatedVisibility(session != null, enter = slideInVertically() + fadeIn(), exit = slideOutVertically() + fadeOut()) {
                Row(
                    Modifier.padding(horizontal = 16.dp, vertical = 6.dp).fillMaxWidth()
                        .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.16f), RoundedCornerShape(14.dp)).padding(12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text(stringResource(R.string.session_active), fontWeight = FontWeight.Bold)
                        Muted(stringResource(R.string.session_desc), size = 12)
                    }
                    Button(onClick = { GameSessionService.stop(ctx) }, colors = ButtonDefaults.buttonColors(containerColor = Palette.Surface2)) {
                        Text(stringResource(R.string.session_end))
                    }
                }
            }
            AnimatedContent(tab, transitionSpec = { (fadeIn() + slideInVertically { it / 12 }) togetherWith fadeOut() }, label = "tab") { t ->
                Box(Modifier.fillMaxSize()) {
                    when (t) {
                        0 -> GamesScreen(prefs)
                        1 -> CrosshairScreen(prefs)
                        2 -> NetworkScreen()
                        else -> SettingsScreen(prefs, onAccent)
                    }
                }
            }
        }
    }
}

@Composable
private fun Header() {
    val a = MaterialTheme.colorScheme.primary
    val b = MaterialTheme.colorScheme.secondary
    Row(Modifier.padding(start = 16.dp, end = 16.dp, top = 10.dp, bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(34.dp).background(accentBrush(a, b), RoundedCornerShape(10.dp)), contentAlignment = Alignment.Center) {
            Icon(Glyphs.Gamepad, null, Modifier.size(20.dp), tint = androidx.compose.ui.graphics.Color.White)
        }
        Spacer(Modifier.width(10.dp))
        Column {
            Row {
                Text("Boost", fontSize = 20.sp, fontWeight = FontWeight.ExtraBold)
                Text("Root", fontSize = 20.sp, fontWeight = FontWeight.ExtraBold, color = b)
            }
            Muted(stringResource(R.string.tagline), size = 11)
        }
    }
}

@Composable
fun Toggle(text: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
        Text(text, Modifier.weight(1f), fontSize = 15.sp)
        androidx.compose.material3.Switch(checked, onChange)
    }
}
