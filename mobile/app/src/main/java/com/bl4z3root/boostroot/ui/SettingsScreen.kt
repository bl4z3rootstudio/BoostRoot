package com.bl4z3root.boostroot.ui

import android.app.Activity
import android.content.Intent
import android.net.Uri
import androidx.appcompat.app.AppCompatDelegate
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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.os.LocaleListCompat
import com.bl4z3root.boostroot.BuildConfig
import com.bl4z3root.boostroot.R
import com.bl4z3root.boostroot.ads.Ads
import com.bl4z3root.boostroot.platform.Permissions
import com.bl4z3root.boostroot.platform.Prefs

/** Languages are shown in their own name, like the PC app. */
private val LANGS = listOf(
    "" to null, "tr" to "Türkçe", "en" to "English", "az" to "Azərbaycanca", "de" to "Deutsch", "ru" to "Русский",
    "es" to "Español", "pt" to "Português", "fr" to "Français", "ar" to "العربية",
)

const val PRIVACY_URL = "https://bl4z3rootstudio.github.io/BoostRoot/mobile-privacy.html"
const val PC_URL = "https://github.com/bl4z3rootstudio/BoostRoot/releases/latest"

@Composable
fun SettingsScreen(prefs: Prefs, onAccent: (Int) -> Unit) {
    val ctx = LocalContext.current
    val current = AppCompatDelegate.getApplicationLocales().toLanguageTags().substringBefore('-')
    val privacyNeeded by Ads.privacyOptionsRequired.collectAsState()
    var accent by remember { mutableIntStateOf(prefs.accent) }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        SectionTitle(stringResource(R.string.settings_language))
        Card {
            LANGS.forEach { (tag, name) ->
                val selected = tag == current
                Row(
                    Modifier.fillMaxWidth().clickable { AppCompatDelegate.setApplicationLocales(LocaleListCompat.forLanguageTags(tag)) }
                        .background(if (selected) MaterialTheme.colorScheme.primary.copy(alpha = 0.15f) else Color.Transparent, RoundedCornerShape(10.dp))
                        .padding(horizontal = 10.dp, vertical = 10.dp),
                ) {
                    Text(name ?: stringResource(R.string.settings_system_language), fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal)
                }
            }
        }

        SectionTitle(stringResource(R.string.settings_accent))
        Card {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Palette.accents.forEachIndexed { i, (c, _) ->
                    Box(
                        Modifier.size(36.dp).background(c, CircleShape).border(if (i == accent) 3.dp else 0.dp, Color.White, CircleShape)
                            .clickable { accent = i; onAccent(i) },
                    )
                }
            }
        }

        SectionTitle(stringResource(R.string.settings_permissions))
        Card {
            PermStatus(R.string.perm_overlay_title, Permissions.canOverlay(ctx)) { ctx.startActivity(Permissions.overlayIntent(ctx)) }
            PermStatus(R.string.perm_dnd_title, Permissions.hasDnd(ctx)) { ctx.startActivity(Permissions.dndIntent()) }
        }

        SectionTitle(stringResource(R.string.settings_about))
        Card {
            Text(stringResource(R.string.settings_version, BuildConfig.VERSION_NAME), fontSize = 14.sp)
            Spacer(Modifier.height(8.dp))
            Link(R.string.settings_rate) { open(ctx, "https://play.google.com/store/apps/details?id=" + ctx.packageName) }
            Link(R.string.settings_pc) { open(ctx, PC_URL) }
            Link(R.string.settings_privacy_policy) { open(ctx, PRIVACY_URL) }
            if (privacyNeeded) Link(R.string.settings_privacy) { (ctx as? Activity)?.let(Ads::showPrivacyOptions) }
        }
    }
}

@Composable
private fun PermStatus(title: Int, ok: Boolean, onGrant: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
        Text(stringResource(title), Modifier.weight(1f), fontSize = 14.sp)
        if (ok) Text(stringResource(R.string.perm_ok), color = Palette.Good, fontSize = 14.sp)
        else Text(stringResource(R.string.perm_grant), Modifier.clickable(onClick = onGrant), color = MaterialTheme.colorScheme.secondary, fontWeight = FontWeight.Bold)
    }
}

@Composable
private fun Link(text: Int, onClick: () -> Unit) =
    Text(stringResource(text), Modifier.fillMaxWidth().clickable(onClick = onClick).padding(vertical = 8.dp), color = MaterialTheme.colorScheme.secondary, fontWeight = FontWeight.SemiBold)

private fun open(ctx: android.content.Context, url: String) =
    runCatching { ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
