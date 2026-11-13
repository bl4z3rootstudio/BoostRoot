package com.bl4z3root.boostroot

import android.app.Application
import android.os.Bundle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.appcompat.app.AppCompatActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.bl4z3root.boostroot.ads.Ads
import com.bl4z3root.boostroot.platform.Prefs
import com.bl4z3root.boostroot.ui.App
import com.bl4z3root.boostroot.ui.BoostTheme

class BoostRootApp : Application()

class MainActivity : AppCompatActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        Ads.gatherConsent(this)
        val prefs = Prefs(this)
        setContent {
            var accent by remember { mutableIntStateOf(prefs.accent) }
            BoostTheme(accent) {
                App(prefs, onAccent = { accent = it; prefs.accent = it })
            }
        }
    }
}
