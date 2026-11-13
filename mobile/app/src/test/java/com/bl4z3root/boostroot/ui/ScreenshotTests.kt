package com.bl4z3root.boostroot.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import androidx.test.core.app.ApplicationProvider
import com.bl4z3root.boostroot.platform.Prefs
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/** Renders each screen to build/shots (PNG) so the UI can be checked without a phone. */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [36], qualifiers = "w400dp-h860dp-xxhdpi")
class ScreenshotTests {
    @get:Rule val rule = createComposeRule()

    private fun shot(name: String, content: @Composable () -> Unit) {
        rule.setContent { BoostTheme(0) { Box(Modifier.fillMaxSize().background(Palette.Bg)) { content() } } }
        rule.onRoot().captureRoboImage("build/shots/$name.png")
    }

    private val prefs get() = Prefs(ApplicationProvider.getApplicationContext())

    @Test fun games() = shot("games") { GamesScreen(prefs) }
    @Test fun crosshair() = shot("crosshair") { CrosshairScreen(prefs) }
    @Test fun network() = shot("network") { NetworkScreen() }
    @Test fun settings() = shot("settings") { SettingsScreen(prefs) {} }
    @Test fun app() = shot("app") { App(prefs) {} }
    @Test @Config(qualifiers = "+tr") fun appTr() = shot("app-tr") { App(prefs) {} }
    @Test @Config(qualifiers = "+ar") fun crosshairAr() = shot("crosshair-ar") { CrosshairScreen(prefs) }
}
