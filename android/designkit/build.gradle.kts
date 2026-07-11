// android/designkit/build.gradle.kts — SLICE_S7_CONTRACT.md §9a. Tokens, component kit, StateView +
// the 23-state catalog, the ONE LimitReached surface, the ONE generic Problem surface, Iconography with
// a bundled fallback glyph set (Correction 2). No feature module may depend on another feature module;
// this module has zero dependencies on :appshell or :features:* (dependency-direction, §1b).

plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("io.github.takahirom.roborazzi")
}

android {
    namespace = "app.client.designkit"
    compileSdk = 34

    defaultConfig {
        minSdk = 28
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    // Same flavor dimension as :app (empty per-flavor blocks): a library needs no per-flavor source,
    // but declaring the dimension gives every module a `test<Flavor>DebugUnitTest` task so the ONE CI
    // command (`./gradlew test${flavor}DebugUnitTest`) walks this module's tests too.
    flavorDimensions += "brand"
    productFlavors {
        create("weeb") { dimension = "brand" }
        create("friki") { dimension = "brand" }
    }

    buildFeatures {
        compose = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        unitTests {
            isIncludeAndroidResources = true
            // REPO_ROOT / ANDROID_ROOT set per-module (not via a root subprojects{} block, which
            // breaks AGP config). NoDisabledTokenTest reads REPO_ROOT to load design/tokens.v1.json.
            all {
                it.systemProperty("robolectric.pixelCopyRenderMode", "hardware")
                it.systemProperty("REPO_ROOT", rootProject.projectDir.parentFile.absolutePath)
                it.systemProperty("ANDROID_ROOT", rootProject.projectDir.absolutePath)
            }
        }
    }
}

kotlin {
    jvmToolchain(17)
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.09.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.ui:ui-tooling-preview")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.jetbrains.kotlin:kotlin-test:2.0.20")
    testImplementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.1")
    testImplementation("org.robolectric:robolectric:4.14.1")
    testImplementation("androidx.compose.ui:ui-test-junit4")
    testImplementation("androidx.test.ext:junit:1.2.1")
    testImplementation("io.github.takahirom.roborazzi:roborazzi:1.30.1")
    testImplementation("io.github.takahirom.roborazzi:roborazzi-compose:1.30.1")
    testImplementation("io.github.takahirom.roborazzi:roborazzi-junit-rule:1.30.1")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}
