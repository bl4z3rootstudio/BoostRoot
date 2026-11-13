package com.bl4z3root.boostroot.platform

import android.app.NotificationManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.drawable.Drawable
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.os.BatteryManager
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import com.bl4z3root.boostroot.core.CrosshairDesign
import com.bl4z3root.boostroot.core.GameCatalog

/** Small typed wrapper over SharedPreferences. Nothing ever leaves the phone. */
class Prefs(context: Context) {
    private val p = context.applicationContext.getSharedPreferences("boostroot", Context.MODE_PRIVATE)

    var crosshair: CrosshairDesign
        get() = p.getString("crosshair", null)?.let(CrosshairDesign::deserialize) ?: CrosshairDesign.PRESETS[0]
        set(v) = p.edit().putString("crosshair", CrosshairDesign.serialize(v)).apply()
    var accent: Int
        get() = p.getInt("accent", 0)
        set(v) = p.edit().putInt("accent", v).apply()
    var gmDnd: Boolean
        get() = p.getBoolean("gm_dnd", true)
        set(v) = p.edit().putBoolean("gm_dnd", v).apply()
    var gmCrosshair: Boolean
        get() = p.getBoolean("gm_crosshair", false)
        set(v) = p.edit().putBoolean("gm_crosshair", v).apply()
    var gmScreen: Boolean
        get() = p.getBoolean("gm_screen", true)
        set(v) = p.edit().putBoolean("gm_screen", v).apply()
    var premiumUntil: Long
        get() = p.getLong("premium_until", 0)
        set(v) = p.edit().putLong("premium_until", v).apply()
    val premium: Boolean get() = System.currentTimeMillis() < premiumUntil

    /** DND filter that was active before game mode, so we can put it back exactly. -1 = we didn't change it. */
    var savedFilter: Int
        get() = p.getInt("saved_filter", -1)
        set(v) = p.edit().putInt("saved_filter", v).apply()
}

data class InstalledGame(val packageName: String, val label: String, val icon: Drawable?)

object Games {
    /** Apps with a launcher icon that are either known games or marked as games by their developer. */
    fun scan(context: Context): List<InstalledGame> {
        val pm = context.packageManager
        val launcher = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
        val apps = pm.queryIntentActivities(launcher, PackageManager.MATCH_ALL)
        return apps.asSequence()
            .map { it.activityInfo.applicationInfo }
            .distinctBy { it.packageName }
            .filter { it.packageName != context.packageName }
            .filter { GameCatalog.find(it.packageName) != null || isGame(it) }
            .map { InstalledGame(it.packageName, GameCatalog.find(it.packageName)?.name ?: pm.getApplicationLabel(it).toString(), runCatching { pm.getApplicationIcon(it) }.getOrNull()) }
            .sortedWith(compareBy({ GameCatalog.find(it.packageName) == null }, { it.label.lowercase() }))
            .toList()
    }

    @Suppress("DEPRECATION")
    private fun isGame(ai: ApplicationInfo): Boolean =
        ai.category == ApplicationInfo.CATEGORY_GAME || (ai.flags and ApplicationInfo.FLAG_IS_GAME) != 0

    fun launch(context: Context, packageName: String): Boolean {
        val i = context.packageManager.getLaunchIntentForPackage(packageName) ?: return false
        context.startActivity(i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        return true
    }
}

object Permissions {
    fun canOverlay(context: Context) = Settings.canDrawOverlays(context)
    fun hasDnd(context: Context) = context.getSystemService(NotificationManager::class.java).isNotificationPolicyAccessGranted

    fun overlayIntent(context: Context) =
        Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, android.net.Uri.parse("package:" + context.packageName))
    fun dndIntent() = Intent(Settings.ACTION_NOTIFICATION_POLICY_ACCESS_SETTINGS)
}

/** Do Not Disturb on/off, remembering what the user had before. */
object Dnd {
    fun enable(context: Context, prefs: Prefs) {
        val nm = context.getSystemService(NotificationManager::class.java)
        if (!nm.isNotificationPolicyAccessGranted) return
        if (prefs.savedFilter == -1) prefs.savedFilter = nm.currentInterruptionFilter
        nm.setInterruptionFilter(NotificationManager.INTERRUPTION_FILTER_PRIORITY)
    }

    fun restore(context: Context, prefs: Prefs) {
        val saved = prefs.savedFilter
        if (saved == -1) return
        val nm = context.getSystemService(NotificationManager::class.java)
        if (nm.isNotificationPolicyAccessGranted) runCatching { nm.setInterruptionFilter(saved) }
        prefs.savedFilter = -1
    }
}

data class Health(val tempC: Double?, val level: Int?, val charging: Boolean, val thermal: Int)

object DeviceHealth {
    fun read(context: Context): Health {
        val b = context.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        val temp = b?.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, Int.MIN_VALUE)?.takeIf { it != Int.MIN_VALUE }?.div(10.0)
        val lvl = b?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1
        val scale = b?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: -1
        val status = b?.getIntExtra(BatteryManager.EXTRA_STATUS, -1) ?: -1
        val charging = status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL
        val thermal = if (Build.VERSION.SDK_INT >= 29) context.getSystemService(PowerManager::class.java).currentThermalStatus else 0
        return Health(temp, if (lvl >= 0 && scale > 0) lvl * 100 / scale else null, charging, thermal)
    }

    /** 0 cool … 4 critical; combines the OS heat status with battery temperature. */
    fun heatLevel(h: Health): Int {
        val byOs = when (h.thermal) { 0 -> 0; 1 -> 1; 2 -> 2; 3 -> 3; else -> 4 }
        val byTemp = when { h.tempC == null -> 0; h.tempC >= 47 -> 4; h.tempC >= 44 -> 3; h.tempC >= 40 -> 2; h.tempC >= 36 -> 1; else -> 0 }
        return maxOf(byOs, byTemp)
    }
}

enum class NetType { WIFI, MOBILE, NONE }

object Net {
    fun type(context: Context): NetType {
        val cm = context.getSystemService(ConnectivityManager::class.java)
        val caps = cm.getNetworkCapabilities(cm.activeNetwork) ?: return NetType.NONE
        return when {
            caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) -> NetType.WIFI
            caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) -> NetType.MOBILE
            else -> NetType.NONE
        }
    }
}

/** Draws a crosshair centred on (cx, cy). Shared by the in-app preview and the on-screen overlay. */
object CrosshairPainter {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val r = RectF()

    fun draw(canvas: Canvas, cx: Float, cy: Float, d: CrosshairDesign, unit: Float) {
        val u = unit * d.scale
        val color = d.argb
        fun withAlpha(c: Int, a: Double) = (c and 0x00FFFFFF) or ((a.coerceIn(0.0, 1.0) * 255).toInt() shl 24)
        val ow = d.outlineThickness * u

        fun arms(l: com.bl4z3root.boostroot.core.CrosshairLines) {
            if (!l.show || l.length <= 0 || l.thickness <= 0) return
            val half = l.thickness * u / 2f
            val a = l.offset * u
            val b = (l.offset + l.length) * u
            val rects = buildList {
                add(RectF(cx + a, cy - half, cx + b, cy + half))   // right
                add(RectF(cx - b, cy - half, cx - a, cy + half))   // left
                add(RectF(cx - half, cy + a, cx + half, cy + b))   // down
                if (!d.hideTop) add(RectF(cx - half, cy - b, cx + half, cy - a)) // up
            }
            if (d.outline) {
                paint.color = withAlpha(0xFF000000.toInt(), d.outlineOpacity * l.opacity)
                rects.forEach { canvas.drawRect(it.left - ow, it.top - ow, it.right + ow, it.bottom + ow, paint) }
            }
            paint.color = withAlpha(color, l.opacity)
            rects.forEach { canvas.drawRect(it, paint) }
        }
        arms(d.outer)
        arms(d.inner)
        if (d.dot) {
            val h = d.dotThickness * u / 2f
            r.set(cx - h, cy - h, cx + h, cy + h)
            if (d.outline) {
                paint.color = withAlpha(0xFF000000.toInt(), d.outlineOpacity * d.dotOpacity)
                canvas.drawRect(r.left - ow, r.top - ow, r.right + ow, r.bottom + ow, paint)
            }
            paint.color = withAlpha(color, d.dotOpacity)
            canvas.drawRect(r, paint)
        }
    }
}
