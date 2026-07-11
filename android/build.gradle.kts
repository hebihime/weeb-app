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

// Every subproject test task gets a deterministic, absolute path to the monorepo root — the same
// value every time, computed once (deterministic-space per CLAUDE.md), so structural/lint-style unit
// tests (dependency-direction, zero-persistence, release-config, brand/token drift, i18n parity) can
// read files across android/, contracts/, brands/, design/, i18n/ without guessing the CWD Gradle
// happens to use for a given AGP/Gradle version.
subprojects {
    tasks.withType<Test>().configureEach {
        systemProperty("REPO_ROOT", rootProject.projectDir.parentFile.absolutePath)
        systemProperty("ANDROID_ROOT", rootProject.projectDir.absolutePath)
    }
}

tasks.register("clean", Delete::class) {
    delete(rootProject.layout.buildDirectory)
}
