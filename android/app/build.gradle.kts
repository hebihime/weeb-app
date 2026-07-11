// android/app/build.gradle.kts — SLICE_S7_CONTRACT.md §1c/§9. Loads brand.properties (the SAME file
// brand-gate.mjs parses) so gradle and brand-gate share one source — never a duplicated literal.

import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("io.github.takahirom.roborazzi")
}

fun loadBrandProperties(flavorDir: String): Properties {
    val props = Properties()
    file("src/$flavorDir/brand.properties").inputStream().use { props.load(it) }
    return props
}

/** display_name is NOT one of brand.properties' five required keys (HARD CONTRACT #2) — read it from
 * the canonical brands/*.json directly, the same file brand-gate treats as the single source of truth. */
fun displayNameOf(brandKey: String): String {
    val json = rootDir.resolve("../brands/$brandKey.json").readText()
    val match = Regex(""""display_name"\s*:\s*"([^"]+)"""").find(json)
        ?: error("brands/$brandKey.json has no display_name")
    return match.groupValues[1]
}

val weebBrand = loadBrandProperties("weeb")
val frikiBrand = loadBrandProperties("friki")

android {
    namespace = "app.client"
    compileSdk = 34

    defaultConfig {
        minSdk = 28
        targetSdk = 34
        versionCode = 1
        versionName = "0.1.0-s7"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    flavorDimensions += "brand"
    productFlavors {
        create("weeb") {
            dimension = "brand"
            applicationId = weebBrand.getProperty("application_id")
            resValue("string", "app_name", displayNameOf("weeb"))
            buildConfigField("String", "BRAND_KEY", "\"${weebBrand.getProperty("brand_key")}\"")
            buildConfigField("String", "BRAND_WORDMARK", "\"${displayNameOf("weeb")}\"")
            buildConfigField("String", "BRAND_PRIMARY_HEX", "\"${weebBrand.getProperty("brand_primary")}\"")
            buildConfigField("String", "BRAND_CELEBRATION_HEX", "\"${weebBrand.getProperty("brand_celebration")}\"")
            buildConfigField("String", "STRING_PACK_ID", "\"${weebBrand.getProperty("string_pack_id")}\"")
        }
        create("friki") {
            dimension = "brand"
            applicationId = frikiBrand.getProperty("application_id")
            resValue("string", "app_name", displayNameOf("friki"))
            buildConfigField("String", "BRAND_KEY", "\"${frikiBrand.getProperty("brand_key")}\"")
            buildConfigField("String", "BRAND_WORDMARK", "\"${displayNameOf("friki")}\"")
            buildConfigField("String", "BRAND_PRIMARY_HEX", "\"${frikiBrand.getProperty("brand_primary")}\"")
            buildConfigField("String", "BRAND_CELEBRATION_HEX", "\"${frikiBrand.getProperty("brand_celebration")}\"")
            buildConfigField("String", "STRING_PACK_ID", "\"${frikiBrand.getProperty("string_pack_id")}\"")
        }
    }

    buildTypes {
        debug {
            // The ONLY build type that ever holds a backend URL — src/debug is the ONLY sourceSet
            // that ever reads it (the diagnostics screen). Release deliberately declares no such field
            // at all (§9f fail-closed by absence) — see :app's ReleaseConfigTest.
            applicationIdSuffix = ".dev"
            buildConfigField("String", "API_BASE_URL", "\"http://10.0.2.2:8080\"")
        }
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        unitTests {
            isIncludeAndroidResources = true
            isReturnDefaultValues = true
            // REPO_ROOT / ANDROID_ROOT: set here (per-module, inside AGP's own testOptions) rather than
            // via a root `subprojects {}` block — the latter forces cross-project task realization and
            // breaks AGP configuration ("compileSdkVersion is not specified"). The structural tests
            // (DependencyDirection / ZeroPersistence / ReleaseConfig / PlayDataSafety) read these.
            all {
                it.systemProperty("robolectric.pixelCopyRenderMode", "hardware")
                it.systemProperty("REPO_ROOT", rootProject.projectDir.parentFile.absolutePath)
                it.systemProperty("ANDROID_ROOT", rootProject.projectDir.absolutePath)
            }
        }
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

kotlin {
    jvmToolchain(17)
}

dependencies {
    implementation(project(":designkit"))
    implementation(project(":apikit"))
    implementation(project(":appshell"))
    implementation(project(":features:signup"))

    implementation(platform("androidx.compose:compose-bom:2024.09.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.activity:activity-compose:1.9.2")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.4")
    // apikit's generated + hand-written response models are @Serializable (kotlinx.serialization);
    // declared here too (not just transitively via :apikit) so :app's own compile classpath never
    // depends on apikit's `implementation`-scoped choice of serialization library staying exactly as-is.
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.1")
    debugImplementation("androidx.compose.ui:ui-tooling")
    // Provides the test-only host Activity `createComposeRule()` launches under Robolectric — merged
    // into the DEBUG manifest only, which is exactly the variant `test<Flavor>DebugUnitTest` compiles.
    debugImplementation("androidx.compose.ui:ui-test-manifest")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.jetbrains.kotlin:kotlin-test:2.0.20")
    testImplementation("org.robolectric:robolectric:4.14.1")
    testImplementation("androidx.compose.ui:ui-test-junit4")
    testImplementation("io.github.takahirom.roborazzi:roborazzi:1.30.1")
    testImplementation("io.github.takahirom.roborazzi:roborazzi-compose:1.30.1")
    testImplementation("io.github.takahirom.roborazzi:roborazzi-junit-rule:1.30.1")
    testImplementation("androidx.test.ext:junit:1.2.1")
}
