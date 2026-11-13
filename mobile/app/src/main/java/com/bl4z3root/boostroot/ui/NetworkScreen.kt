package com.bl4z3root.boostroot.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.bl4z3root.boostroot.R
import com.bl4z3root.boostroot.core.PingStats
import com.bl4z3root.boostroot.core.PingTargets
import com.bl4z3root.boostroot.core.Pinger
import com.bl4z3root.boostroot.platform.DeviceHealth
import com.bl4z3root.boostroot.platform.Health
import com.bl4z3root.boostroot.platform.Net
import com.bl4z3root.boostroot.platform.NetType
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun NetworkScreen() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var targetIdx by remember { mutableIntStateOf(0) }
    val stats = remember { PingStats(60) }
    var tick by remember { mutableIntStateOf(0) }
    var net by remember { mutableStateOf(NetType.NONE) }
    var health by remember { mutableStateOf<Health?>(null) }
    val all = remember { mutableStateMapOf<String, Double?>() }
    var testing by remember { mutableStateOf(false) }

    LaunchedEffect(targetIdx) {
        stats.clear()
        while (true) {
            val ms = withContext(Dispatchers.IO) { Pinger.measure(PingTargets.all[targetIdx]) }
            stats.add(ms); tick++
            net = Net.type(ctx)
            health = DeviceHealth.read(ctx)
            delay(1000)
        }
    }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        tick.let { }
        val color = when (stats.quality) { 0 -> Palette.Good; 1 -> Palette.Warn; else -> Palette.Bad }
        Card(glow = true) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Muted(stringResource(R.string.net_ping))
                    Text(stats.last?.let { "${it.toInt()} ms" } ?: stringResource(R.string.net_lost), fontSize = 40.sp, fontWeight = FontWeight.ExtraBold, color = color)
                    Muted(
                        stringResource(R.string.net_jitter, "%.0f".format(stats.jitter)) + "  ·  " +
                            stringResource(R.string.net_loss, "%.0f".format(stats.lossPercent)), size = 12,
                    )
                }
                Column(horizontalAlignment = Alignment.End) {
                    Muted(stringResource(when (net) { NetType.WIFI -> R.string.net_type_wifi; NetType.MOBILE -> R.string.net_type_mobile; else -> R.string.net_type_none }), size = 12)
                    Text(PingTargets.all[targetIdx].label, fontWeight = FontWeight.SemiBold)
                }
            }
            Spacer(Modifier.height(10.dp))
            PingChart(stats.values, color)
            Spacer(Modifier.height(6.dp))
            Text(
                stringResource(when (stats.quality) { 0 -> R.string.net_quality_good; 1 -> R.string.net_quality_ok; else -> R.string.net_quality_bad }),
                color = color, fontWeight = FontWeight.SemiBold, fontSize = 14.sp,
            )
        }

        SectionTitle(stringResource(R.string.net_server))
        Card {
            PingTargets.all.forEachIndexed { i, t ->
                Row(
                    Modifier.fillMaxWidth().clickable { targetIdx = i }
                        .background(if (i == targetIdx) MaterialTheme.colorScheme.primary.copy(alpha = 0.15f) else Palette.Surface, RoundedCornerShape(10.dp))
                        .padding(horizontal = 10.dp, vertical = 9.dp),
                ) {
                    Text(t.label, Modifier.weight(1f), fontWeight = if (i == targetIdx) FontWeight.Bold else FontWeight.Normal)
                    if (all.containsKey(t.id)) Text(all[t.id]?.let { "${it.toInt()} ms" } ?: "—", color = Palette.Muted)
                }
            }
            Spacer(Modifier.height(8.dp))
            OutlinedButton(enabled = !testing, modifier = Modifier.fillMaxWidth(), onClick = {
                testing = true
                scope.launch {
                    val res = PingTargets.all.map { t ->
                        async(Dispatchers.IO) { t.id to (1..3).mapNotNull { Pinger.measure(t) }.minOrNull() }
                    }.awaitAll()
                    res.forEach { (id, ms) -> all[id] = ms }
                    testing = false
                }
            }) { Text(stringResource(if (testing) R.string.net_testing else R.string.net_test_all)) }
        }

        health?.let { h -> HealthCard(h) }
        Muted(stringResource(R.string.net_note), size = 12)
    }
}

@Composable
private fun HealthCard(h: Health) {
    val level = DeviceHealth.heatLevel(h)
    val color = when (level) { 0, 1 -> Palette.Good; 2 -> Palette.Warn; else -> Palette.Bad }
    Card {
        Text(stringResource(R.string.device_title), fontWeight = FontWeight.Bold, fontSize = 16.sp)
        Spacer(Modifier.height(8.dp))
        Row {
            Column(Modifier.weight(1f)) {
                Muted(stringResource(R.string.device_temp), size = 12)
                Text(h.tempC?.let { "%.1f °C".format(it) } ?: "—", fontSize = 22.sp, fontWeight = FontWeight.Bold, color = color)
            }
            Column(Modifier.weight(1f)) {
                Muted(stringResource(R.string.device_heat), size = 12)
                Text(
                    stringResource(when (level) { 0 -> R.string.thermal_cool; 1 -> R.string.thermal_warm; 2 -> R.string.thermal_hot; 3 -> R.string.thermal_very_hot; else -> R.string.thermal_critical }),
                    fontWeight = FontWeight.SemiBold, color = color, fontSize = 14.sp,
                )
            }
        }
        Spacer(Modifier.height(6.dp))
        Muted(
            listOfNotNull(h.level?.let { stringResource(R.string.device_battery, it) }, if (h.charging) stringResource(R.string.device_charging) else null).joinToString("  ·  "),
            size = 12,
        )
    }
}

@Composable
private fun PingChart(values: List<Double?>, color: androidx.compose.ui.graphics.Color) {
    Canvas(Modifier.fillMaxWidth().height(90.dp)) {
        if (values.size < 2) return@Canvas
        val max = (values.filterNotNull().maxOrNull() ?: 100.0).coerceAtLeast(60.0) * 1.15
        val step = size.width / 59f
        val path = Path()
        var started = false
        values.forEachIndexed { i, v ->
            val x = size.width - (values.size - 1 - i) * step
            if (v == null) {
                drawLine(Palette.Bad, Offset(x, 0f), Offset(x, size.height), strokeWidth = 2f)
                started = false
            } else {
                val y = size.height - (v / max * size.height).toFloat()
                if (!started) { path.moveTo(x, y); started = true } else path.lineTo(x, y)
            }
        }
        drawPath(path, color, style = Stroke(width = 4f))
    }
}
