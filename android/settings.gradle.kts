// android/settings.gradle.kts — SLICE_S7_CONTRACT.md §1b/§9.
//
// Module layout mirrors iOS's layer names 1:1 so Maestro flows and reviews transfer:
// DesignKit / ApiKit / AppShell / Features/*. Every module (including :app) carries the same
// "brand" flavor dimension (weeb/friki) so `./gradlew test<Flavor>DebugUnitTest` — the exact
// command android.yml invokes — walks every module's unit tests, not just :app's.

pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "weeb-android"

include(":app")
include(":designkit")
include(":apikit")
include(":appshell")
include(":features:signup")
