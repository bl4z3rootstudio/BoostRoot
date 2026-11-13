package com.bl4z3root.boostroot.platform

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.Canvas
import android.graphics.PixelFormat
import android.os.Build
import android.os.IBinder
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import com.bl4z3root.boostroot.MainActivity
import com.bl4z3root.boostroot.R
import com.bl4z3root.boostroot.core.CrosshairDesign
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/**
 * A game session the user started: optional crosshair overlay, optional Do Not Disturb, optional keep-screen-on.
 * It runs until the user taps "End" (notification or app). Nothing is injected into games: the crosshair is our own
 * window drawn above everything, and it never takes touches.
 */
class GameSessionService : Service() {
    private var overlay: View? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) { stopSelf(); return START_NOT_STICKY }
        val prefs = Prefs(this)
        val dnd = intent?.getBooleanExtra(EXTRA_DND, false) ?: false
        val crosshair = intent?.getBooleanExtra(EXTRA_CROSSHAIR, false) ?: false
        val keepOn = intent?.getBooleanExtra(EXTRA_SCREEN, false) ?: false

        startInForeground()
        if (dnd) Dnd.enable(this, prefs)
        showOverlay(if (crosshair && Permissions.canOverlay(this)) prefs.crosshair else null, keepOn)
        _running.value = Session(dnd, crosshair, keepOn)
        return START_NOT_STICKY
    }

    private fun startInForeground() {
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CHANNEL, getString(R.string.notif_channel), NotificationManager.IMPORTANCE_LOW))
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 1, Intent(this, GameSessionService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE)
        val n = Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_launcher_foreground)
            .setContentTitle(getString(R.string.session_active))
            .setContentText(getString(R.string.notif_text))
            .setContentIntent(open)
            .setOngoing(true)
            .addAction(Notification.Action.Builder(null, getString(R.string.notif_end), stop).build())
            .build()
        if (Build.VERSION.SDK_INT >= 34) startForeground(NOTIF_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        else startForeground(NOTIF_ID, n)
    }

    private fun showOverlay(design: CrosshairDesign?, keepOn: Boolean) {
        removeOverlay()
        if (design == null && !keepOn) return
        if (!Permissions.canOverlay(this)) return
        val wm = getSystemService(WindowManager::class.java)
        val density = resources.displayMetrics.density
        val sizePx = if (design == null) 1 else (220 * density * design.scale).toInt().coerceAtLeast(64)
        var flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS
        if (keepOn) flags = flags or WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON
        val lp = WindowManager.LayoutParams(sizePx, sizePx, WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY, flags, PixelFormat.TRANSLUCENT).apply {
            gravity = Gravity.CENTER
            x = ((design?.offsetX ?: 0) * density).toInt()
            y = ((design?.offsetY ?: 0) * density).toInt()
            // Android 12+: touches only pass through other apps' windows at ≤ 80 % opacity.
            alpha = 0.8f
            if (Build.VERSION.SDK_INT >= 28) layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
        }
        val view = object : View(this) {
            override fun onDraw(canvas: Canvas) {
                if (design != null) CrosshairPainter.draw(canvas, width / 2f, height / 2f, design, 1.5f * density)
            }
        }
        runCatching { wm.addView(view, lp); overlay = view }
    }

    private fun removeOverlay() {
        overlay?.let { v -> runCatching { getSystemService(WindowManager::class.java).removeView(v) } }
        overlay = null
    }

    override fun onDestroy() {
        removeOverlay()
        Dnd.restore(this, Prefs(this))
        _running.value = null
        super.onDestroy()
    }

    data class Session(val dnd: Boolean, val crosshair: Boolean, val keepOn: Boolean)

    companion object {
        private const val CHANNEL = "session"
        private const val NOTIF_ID = 7
        private const val ACTION_STOP = "stop"
        private const val EXTRA_DND = "dnd"
        private const val EXTRA_CROSSHAIR = "crosshair"
        private const val EXTRA_SCREEN = "screen"

        private val _running = MutableStateFlow<Session?>(null)
        val running: StateFlow<Session?> = _running

        fun start(context: Context, dnd: Boolean, crosshair: Boolean, keepOn: Boolean) {
            val i = Intent(context, GameSessionService::class.java)
                .putExtra(EXTRA_DND, dnd).putExtra(EXTRA_CROSSHAIR, crosshair).putExtra(EXTRA_SCREEN, keepOn)
            context.startForegroundService(i)
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, GameSessionService::class.java))
        }
    }
}
