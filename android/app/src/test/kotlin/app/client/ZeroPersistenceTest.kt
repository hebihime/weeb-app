package app.client

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §3 — "a client test asserts no persistence framework (CoreData/SwiftData/
 * Room/DataStore) is linked into either app target." Two scans over the whole android/ tree: no
 * persistence-framework DEPENDENCY declared in any build.gradle(.kts), and no persistence-framework API
 * USED in any hand-written Kotlin source (a dependency could be pulled in transitively without being
 * declared directly; the API-usage scan catches that too).
 */
class ZeroPersistenceTest {
    private val repoRoot = File(System.getProperty("REPO_ROOT") ?: error("REPO_ROOT system property not set"))
    private val androidRoot = File(repoRoot, "android")

    private val bannedDependencyCoordinates = listOf(
        "androidx.room",
        "androidx.datastore",
        "androidx.security:security-crypto",
        "com.squareup.sqldelight",
        "io.realm",
        "net.sqlcipher",
    )

    private val bannedApiUsages = listOf(
        "getSharedPreferences(",
        "PreferenceManager",
        "androidx.datastore",
        "androidx.room",
        "openOrCreateDatabase(",
        "SQLiteOpenHelper",
    )

    // `pick` is the LAST parameter so callers can use trailing-lambda syntax: `walk(dir) { ... }`
    // binds the lambda to `pick`; `skipDirs` keeps its default. (Kotlin binds a trailing lambda to the
    // last parameter — with `pick` in the middle it bound to `skipDirs` and failed to type-check.)
    private fun walk(dir: File, skipDirs: Set<String> = setOf(".git", "build", ".gradle"), pick: (File) -> Boolean): List<File> {
        val out = mutableListOf<File>()
        fun go(d: File) {
            for (f in d.listFiles() ?: emptyArray()) {
                if (f.isDirectory) {
                    if (f.name !in skipDirs) go(f)
                } else if (pick(f)) {
                    out.add(f)
                }
            }
        }
        go(dir)
        return out
    }

    @Test
    fun `no build file declares a persistence-framework dependency`() {
        val buildFiles = walk(androidRoot) { it.name.endsWith(".gradle") || it.name.endsWith(".gradle.kts") }
        val offenders = mutableListOf<String>()
        for (file in buildFiles) {
            val text = file.readText().lowercase()
            for (coord in bannedDependencyCoordinates) {
                if (text.contains(coord.lowercase())) {
                    offenders.add("${file.relativeTo(repoRoot)}: $coord")
                }
            }
        }
        assertTrue(offenders.isEmpty(), "persistence-framework dependency reference(s) found: $offenders")
    }

    /** Excludes this test's own file (and any other unit test source) — a test is allowed to name a
     * banned API as a string literal to check against; only PRODUCTION source is asserted clean. */
    private fun isProductionKotlin(file: File): Boolean =
        file.extension == "kt" && !file.path.replace('\\', '/').contains("/src/test/")

    @Test
    fun `no hand-written Kotlin source calls a persistence-framework API`() {
        val ktFiles = walk(androidRoot) { isProductionKotlin(it) }
        val offenders = mutableListOf<String>()
        for (file in ktFiles) {
            val text = file.readText()
            for (needle in bannedApiUsages) {
                if (text.contains(needle)) {
                    offenders.add("${file.relativeTo(repoRoot)}: $needle")
                }
            }
        }
        assertTrue(offenders.isEmpty(), "persistence-framework API usage found: $offenders")
    }

    @Test
    fun `no signup form state ever leaves memory - the gateway is the only egress and it always fails`() {
        // A structural cross-check, not a duplicate of UnavailableSignupGatewayTest: this asserts the
        // ENTIRE features/signup source tree has zero references to any of the persistence primitives
        // above, i.e. even the feature that collects the most user input has nowhere to write it.
        val signupFiles = walk(File(androidRoot, "features/signup")) { isProductionKotlin(it) }
        assertTrue(signupFiles.isNotEmpty(), "expected features/signup to contain Kotlin sources")
        for (file in signupFiles) {
            val text = file.readText()
            for (needle in bannedApiUsages) {
                assertTrue(!text.contains(needle), "${file.relativeTo(repoRoot)} references $needle")
            }
        }
    }
}
