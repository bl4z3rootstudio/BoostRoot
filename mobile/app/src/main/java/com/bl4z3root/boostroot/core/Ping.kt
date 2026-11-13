package com.bl4z3root.boostroot.core

import java.net.InetSocketAddress
import java.net.Socket

/** A place to measure latency to. Measured with a TCP handshake on 443 (ICMP needs root on Android). */
data class PingTarget(val id: String, val label: String, val host: String, val port: Int = 443)

object PingTargets {
    val all = listOf(
        PingTarget("cloudflare", "Cloudflare", "1.1.1.1"),
        PingTarget("google", "Google", "8.8.8.8"),
        PingTarget("frankfurt", "Frankfurt", "dynamodb.eu-central-1.amazonaws.com"),
        PingTarget("stockholm", "Stockholm", "dynamodb.eu-north-1.amazonaws.com"),
        PingTarget("milan", "Milano", "dynamodb.eu-south-1.amazonaws.com"),
        PingTarget("bahrain", "Bahrain", "dynamodb.me-south-1.amazonaws.com"),
        PingTarget("mumbai", "Mumbai", "dynamodb.ap-south-1.amazonaws.com"),
        PingTarget("singapore", "Singapore", "dynamodb.ap-southeast-1.amazonaws.com"),
        PingTarget("virginia", "N. Virginia", "dynamodb.us-east-1.amazonaws.com"),
        PingTarget("saopaulo", "São Paulo", "dynamodb.sa-east-1.amazonaws.com"),
    )
}

/** Rolling stats over the last [window] samples; null = lost. */
class PingStats(private val window: Int = 60) {
    private val samples = ArrayDeque<Double?>()

    fun add(ms: Double?) {
        samples.addLast(ms)
        while (samples.size > window) samples.removeFirst()
    }

    val values: List<Double?> get() = samples.toList()
    val last: Double? get() = samples.lastOrNull()
    val lossPercent: Double get() = if (samples.isEmpty()) 0.0 else 100.0 * samples.count { it == null } / samples.size
    val average: Double? get() = samples.filterNotNull().takeIf { it.isNotEmpty() }?.average()
    val jitter: Double
        get() {
            val ok = samples.filterNotNull()
            return if (ok.size < 2) 0.0 else ok.zipWithNext { a, b -> kotlin.math.abs(a - b) }.average()
        }

    /** 0 = great, 1 = okay, 2 = bad — drives the colour of the ping card. */
    val quality: Int
        get() {
            val avg = average ?: return 2
            return when {
                lossPercent >= 3 || avg > 120 || jitter > 25 -> 2
                lossPercent > 0 || avg > 60 || jitter > 10 -> 1
                else -> 0
            }
        }

    fun clear() = samples.clear()
}

object Pinger {
    /** One measurement; blocking, call off the main thread. Returns null on timeout/failure. */
    fun measure(target: PingTarget, timeoutMs: Int = 1500): Double? = runCatching {
        Socket().use { s ->
            val start = System.nanoTime()
            s.connect(InetSocketAddress(target.host, target.port), timeoutMs)
            (System.nanoTime() - start) / 1_000_000.0
        }
    }.getOrNull()
}
