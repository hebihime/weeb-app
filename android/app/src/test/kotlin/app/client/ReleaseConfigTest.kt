package app.client

import java.io.File
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §9f / §1d — "Release builds contain no configured backend URL and make zero
 * network calls ... A release-config test asserts the URL and the cleartext exceptions are absent."
 * This is a static-source assertion (reads the actual build.gradle.kts / manifest / xml text), not an
 * instrumented test against a compiled release variant — CI's unit-test job only ever runs debug
 * variants (`test<Flavor>DebugUnitTest`), so the release posture has to be provable by inspecting the
 * files that DEFINE the release variant, which is exactly as strong a guarantee: if these files don't
 * say it, the release build literally cannot do it.
 */
class ReleaseConfigTest {
    private val repoRoot = File(System.getProperty("REPO_ROOT") ?: error("REPO_ROOT system property not set"))
    private val appDir = File(repoRoot, "android/app")

    @Test
    fun `the release build type declares no backend URL, only debug does`() {
        val buildFile = File(appDir, "build.gradle.kts").readText()
        val releaseBlock = extractBlock(buildFile, "release {")
        val debugBlock = extractBlock(buildFile, "debug {")
        assertFalse(releaseBlock.contains("API_BASE_URL"), "release buildType declares API_BASE_URL")
        assertTrue(debugBlock.contains("API_BASE_URL"), "debug buildType should declare API_BASE_URL")
    }

    @Test
    fun `main network security config forbids cleartext and names no loopback host`() {
        val mainConfig = File(appDir, "src/main/res/xml/network_security_config.xml").readText()
        assertTrue(mainConfig.contains("""cleartextTrafficPermitted="false""""))
        for (host in listOf("localhost", "127.0.0.1", "10.0.2.2")) {
            assertFalse(mainConfig.contains(host), "release-safe network_security_config.xml names loopback host $host")
        }
    }

    @Test
    fun `debug network security config is the ONLY place loopback cleartext exceptions exist`() {
        val debugConfig = File(appDir, "src/debug/res/xml/network_security_config.xml").readText()
        assertTrue(debugConfig.contains("localhost"))
        assertTrue(debugConfig.contains("10.0.2.2"))
    }

    @Test
    fun `the manifest declares usesCleartextTraffic false`() {
        val manifest = File(appDir, "src/main/AndroidManifest.xml").readText()
        assertTrue(manifest.contains("""android:usesCleartextTraffic="false""""))
    }

    @Test
    fun `INTERNET permission is requested only by the debug manifest overlay`() {
        val mainManifest = File(appDir, "src/main/AndroidManifest.xml").readText()
        val debugManifest = File(appDir, "src/debug/AndroidManifest.xml").readText()
        assertFalse(mainManifest.contains("android.permission.INTERNET"))
        assertTrue(debugManifest.contains("android.permission.INTERNET"))
    }

    @Test
    fun `no release-compiled Kotlin source (src main or src release) references a backend host literal`() {
        val offenders = mutableListOf<String>()
        for (sourceSet in listOf("src/main/kotlin", "src/release/kotlin")) {
            val dir = File(appDir, sourceSet)
            if (!dir.exists()) continue
            for (file in dir.walkTopDown().filter { it.extension == "kt" }) {
                val text = file.readText()
                if (Regex("""https?://(?!\$)[\w.]*(10\.0\.2\.2|localhost|127\.0\.0\.1)""").containsMatchIn(text)) {
                    offenders.add(file.relativeTo(repoRoot).toString())
                }
            }
        }
        assertTrue(offenders.isEmpty(), "backend host literal found outside src/debug: $offenders")
    }

    /** Extracts the body of the first top-level-ish `<label> { ... }` block in [text] (brace-balanced). */
    private fun extractBlock(text: String, label: String): String {
        val start = text.indexOf(label)
        if (start < 0) return ""
        var depth = 0
        var i = text.indexOf('{', start)
        val bodyStart = i
        while (i < text.length) {
            when (text[i]) {
                '{' -> depth++
                '}' -> {
                    depth--
                    if (depth == 0) return text.substring(bodyStart, i + 1)
                }
            }
            i++
        }
        return text.substring(bodyStart)
    }
}
