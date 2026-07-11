// android/build.gradle.kts — SLICE_S7_CONTRACT.md §1a: Gradle 8.7+, AGP 8.5+, Kotlin 2.0+, Compose,
// minSdk 28. Plugin versions pinned once here; every module applies without a version (Gradle's
// plugin-management version resolution) so the whole tree upgrades from one place.

plugins {
    id("com.android.application") version "8.5.2" apply false
    id("com.android.library") version "8.5.2" apply false
    id("org.jetbrains.kotlin.android") version "2.0.20" apply false
    id("org.jetbrains.kotlin.plugin.compose") version "2.0.20" apply false
    id("org.jetbrains.kotlin.plugin.serialization") version "2.0.20" apply false
    id("org.openapi.generator") version "7.9.0" apply false
    id("io.github.takahirom.roborazzi") version "1.30.1" apply false
}

// NOTE: the REPO_ROOT / ANDROID_ROOT test system properties are set INSIDE each module's own
// build.gradle.kts (android { testOptions { unitTests { all { ... } } } }), NOT here via a
// `subprojects { tasks.withType<Test>() ... }` block. Touching `.tasks` on a subproject from a root
// `subprojects {}`/`allprojects {}` block is cross-project configuration that forces AGP's
// createAndroidTasks (an afterEvaluate action) to run before the module's `android { }` extension is
// configured — which fails configuration with "compileSdkVersion is not specified". Per-module config
// keeps every module's AGP lifecycle intact. Structural tests (dependency-direction, zero-persistence,
// release-config, brand/token/i18n parity, contract-shape, no-hand-rolled-request-model) still receive
// both properties from their own module (app / apikit / designkit) — the only modules whose tests read
// them.

tasks.register("clean", Delete::class) {
    delete(rootProject.layout.buildDirectory)
}
