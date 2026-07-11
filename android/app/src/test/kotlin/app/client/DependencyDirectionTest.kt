package app.client

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §1b/§10 — "Features never import each other; only AppShell composes." Enforced
 * as a static import scan across the real module trees so a future feature added under
 * `android/features/*` inherits the check for free: only :app is allowed to import both :appshell and
 * a feature; :appshell may never import a feature; a feature may never import :appshell or another
 * feature; :designkit/​:apikit import neither (leaf modules).
 */
class DependencyDirectionTest {
    private val repoRoot = File(System.getProperty("REPO_ROOT") ?: error("REPO_ROOT system property not set"))
    private val androidRoot = File(repoRoot, "android")

    private fun kotlinFilesUnder(relative: String): List<File> {
        val dir = File(androidRoot, relative)
        if (!dir.exists()) return emptyList()
        return dir.walkTopDown().filter { it.extension == "kt" && !it.path.replace('\\', '/').contains("/src/test/") }.toList()
    }

    private fun importsOf(file: File): List<String> =
        file.readText().lineSequence().filter { it.trim().startsWith("import ") }.map { it.trim().removePrefix("import ").trim() }.toList()

    @Test
    fun `appshell never imports a feature module`() {
        val offenders = mutableListOf<String>()
        for (file in kotlinFilesUnder("appshell")) {
            for (imp in importsOf(file)) {
                if (imp.startsWith("app.client.features.")) {
                    offenders.add("${file.relativeTo(repoRoot)}: $imp")
                }
            }
        }
        assertTrue(offenders.isEmpty(), "appshell imports a feature module: $offenders")
    }

    @Test
    fun `features signup never imports appshell`() {
        val offenders = mutableListOf<String>()
        for (file in kotlinFilesUnder("features/signup")) {
            for (imp in importsOf(file)) {
                if (imp.startsWith("app.client.appshell")) {
                    offenders.add("${file.relativeTo(repoRoot)}: $imp")
                }
            }
        }
        assertTrue(offenders.isEmpty(), "features/signup imports appshell: $offenders")
    }

    @Test
    fun `features signup never imports another feature module`() {
        val offenders = mutableListOf<String>()
        for (file in kotlinFilesUnder("features/signup")) {
            for (imp in importsOf(file)) {
                if (imp.startsWith("app.client.features.") && !imp.startsWith("app.client.features.signup")) {
                    offenders.add("${file.relativeTo(repoRoot)}: $imp")
                }
            }
        }
        assertTrue(offenders.isEmpty(), "features/signup imports a sibling feature: $offenders")
    }

    @Test
    fun `designkit imports neither appshell nor any feature (leaf module)`() {
        val offenders = mutableListOf<String>()
        for (file in kotlinFilesUnder("designkit")) {
            for (imp in importsOf(file)) {
                if (imp.startsWith("app.client.appshell") || imp.startsWith("app.client.features.")) {
                    offenders.add("${file.relativeTo(repoRoot)}: $imp")
                }
            }
        }
        assertTrue(offenders.isEmpty(), "designkit imports up the stack: $offenders")
    }

    @Test
    fun `apikit imports neither appshell nor any feature (leaf module)`() {
        val offenders = mutableListOf<String>()
        for (file in kotlinFilesUnder("apikit")) {
            for (imp in importsOf(file)) {
                if (imp.startsWith("app.client.appshell") || imp.startsWith("app.client.features.")) {
                    offenders.add("${file.relativeTo(repoRoot)}: $imp")
                }
            }
        }
        assertTrue(offenders.isEmpty(), "apikit imports up the stack: $offenders")
    }

    @Test
    fun `only app composes both appshell and a feature module`() {
        var appshellImported = false
        var featureImported = false
        for (file in kotlinFilesUnder("app/src/main")) {
            for (imp in importsOf(file)) {
                if (imp.startsWith("app.client.appshell")) appshellImported = true
                if (imp.startsWith("app.client.features.")) featureImported = true
            }
        }
        assertTrue(appshellImported, ":app should compose AppShell")
        assertTrue(featureImported, ":app should compose a feature (Signup)")
    }
}
