plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

// Version follows the PC app: YY.M.N → versionCode YYMMNN
val appVersion = (project.findProperty("appVersion") as String?) ?: "26.11.1"
val parts = appVersion.split(".").map { it.toInt() }

android {
    namespace = "com.bl4z3root.boostroot"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.bl4z3root.boostroot"
        minSdk = 26
        targetSdk = 36
        versionName = appVersion
        versionCode = parts[0] * 10000 + parts[1] * 100 + parts[2]
        // Google's public TEST ids. Real ids come from gradle properties at release time.
        manifestPlaceholders["admobAppId"] = (project.findProperty("admobAppId") as String?) ?: "ca-app-pub-3940256099942544~3347511713"
        buildConfigField("String", "BANNER_ID", "\"${(project.findProperty("admobBannerId") as String?) ?: "ca-app-pub-3940256099942544/9214589741"}\"")
        buildConfigField("String", "REWARDED_ID", "\"${(project.findProperty("admobRewardedId") as String?) ?: "ca-app-pub-3940256099942544/5224354917"}\"")
    }

    signingConfigs {
        create("release") {
            val ks = System.getenv("BR_KEYSTORE")
            if (ks != null && file(ks).exists()) {
                storeFile = file(ks)
                storePassword = System.getenv("BR_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("BR_KEY_ALIAS") ?: "boostroot"
                keyPassword = System.getenv("BR_KEY_PASSWORD") ?: System.getenv("BR_KEYSTORE_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            if (System.getenv("BR_KEYSTORE") != null) signingConfig = signingConfigs.getByName("release")
        }
    }
    buildFeatures { compose = true; buildConfig = true }
    androidResources { generateLocaleConfig = true }
    testOptions {
        unitTests {
            isIncludeAndroidResources = true
            all { it.jvmArgs("--add-opens=java.base/java.io=ALL-UNNAMED", "--add-opens=java.base/java.lang=ALL-UNNAMED", "--add-exports=java.base/jdk.internal.access=ALL-UNNAMED", "--add-opens=java.base/jdk.internal.access=ALL-UNNAMED"); it.maxHeapSize = "2g"; it.systemProperty("roborazzi.test.record", "true"); it.systemProperty("robolectric.dependency.repo.url", "https://maven-central.storage-download.googleapis.com/maven2/") }
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    val bom = platform("androidx.compose:compose-bom:2026.09.00")
    implementation(bom)
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material:material-icons-core")
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.appcompat:appcompat:1.8.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.11.0")
    implementation("com.google.android.gms:play-services-ads:25.5.0")
    implementation("com.google.android.ump:user-messaging-platform:4.0.0")
    debugImplementation("androidx.compose.ui:ui-tooling")
    testImplementation("junit:junit:4.13.2")
    // Screenshot tests (no emulator needed): Robolectric renders the real Compose UI to PNGs.
    testImplementation("org.robolectric:robolectric:4.17")
    testImplementation("io.github.takahirom.roborazzi:roborazzi:1.79.0")
    testImplementation("io.github.takahirom.roborazzi:roborazzi-compose:1.79.0")
    testImplementation("androidx.compose.ui:ui-test-junit4")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}
