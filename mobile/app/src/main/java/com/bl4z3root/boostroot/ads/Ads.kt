package com.bl4z3root.boostroot.ads

import android.app.Activity
import android.content.Context
import android.util.DisplayMetrics
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.viewinterop.AndroidView
import com.bl4z3root.boostroot.BuildConfig
import com.google.android.gms.ads.AdRequest
import com.google.android.gms.ads.AdSize
import com.google.android.gms.ads.AdView
import com.google.android.gms.ads.LoadAdError
import com.google.android.gms.ads.MobileAds
import com.google.android.gms.ads.rewarded.RewardedAd
import com.google.android.gms.ads.rewarded.RewardedAdLoadCallback
import com.google.android.ump.ConsentInformation
import com.google.android.ump.ConsentRequestParameters
import com.google.android.ump.UserMessagingPlatform
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.flow.MutableStateFlow

/**
 * Ads only inside our own screens (never over games). Consent (GDPR/UMP) is asked first; ads load only when allowed.
 */
object Ads {
    private val started = AtomicBoolean(false)
    val ready = MutableStateFlow(false)
    val privacyOptionsRequired = MutableStateFlow(false)
    private var rewarded: RewardedAd? = null

    fun gatherConsent(activity: Activity) {
        val info = UserMessagingPlatform.getConsentInformation(activity)
        info.requestConsentInfoUpdate(activity, ConsentRequestParameters.Builder().build(), {
            UserMessagingPlatform.loadAndShowConsentFormIfRequired(activity) {
                privacyOptionsRequired.value =
                    info.privacyOptionsRequirementStatus == ConsentInformation.PrivacyOptionsRequirementStatus.REQUIRED
                if (info.canRequestAds()) init(activity)
            }
        }, { if (info.canRequestAds()) init(activity) })
        if (info.canRequestAds()) init(activity)
    }

    fun showPrivacyOptions(activity: Activity) = UserMessagingPlatform.showPrivacyOptionsForm(activity) {}

    private fun init(context: Context) {
        if (!started.compareAndSet(false, true)) return
        MobileAds.initialize(context.applicationContext) { ready.value = true; loadRewarded(context) }
    }

    private fun loadRewarded(context: Context) {
        RewardedAd.load(context, BuildConfig.REWARDED_ID, AdRequest.Builder().build(), object : RewardedAdLoadCallback() {
            override fun onAdLoaded(ad: RewardedAd) { rewarded = ad }
            override fun onAdFailedToLoad(error: LoadAdError) { rewarded = null }
        })
    }

    val hasRewarded: Boolean get() = rewarded != null

    /** Shows the rewarded ad; [onReward] runs only if the user watched it. */
    fun showRewarded(activity: Activity, onReward: () -> Unit): Boolean {
        val ad = rewarded ?: return false
        rewarded = null
        ad.show(activity) { onReward() }
        loadRewarded(activity)
        return true
    }
}

@Composable
fun BannerAd(modifier: Modifier = Modifier) {
    AndroidView(modifier = modifier.fillMaxWidth(), factory = { ctx ->
        AdView(ctx).apply {
            val dm: DisplayMetrics = ctx.resources.displayMetrics
            setAdSize(AdSize.getCurrentOrientationAnchoredAdaptiveBannerAdSize(ctx, (dm.widthPixels / dm.density).toInt()))
            adUnitId = BuildConfig.BANNER_ID
            loadAd(AdRequest.Builder().build())
        }
    })
}
