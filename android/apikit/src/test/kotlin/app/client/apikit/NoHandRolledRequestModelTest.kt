package app.client.apikit

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §9f — "zero non-generated request models exist in ApiKit consumers (lint)."
 * S7 ships zero request bodies (both consumed operations are bodiless GETs, §1d): no hand-written
 * PRODUCTION source may declare a Request-shaped model class. A future slice that legitimately needs
 * one gets it from generated code (openApiGenerate), never a hand-typed `data class FooRequest`.
 *
 * Scans PRODUCTION source only (`src/main`), never `src/test` — a test is free to name itself
 * "…RequestModelTest" or discuss "data class FooRequest" in a comment without being a violation; the
 * rule is about shipped models. Comments and string literals are stripped before matching so a doc
 * comment or log message mentioning a Request class is never a false positive. Generated code lives
 * under `build/` and is excluded.
 */
class NoHandRolledRequestModelTest {
    private val repoRoot = File(System.getProperty("REPO_ROOT") ?: error("REPO_ROOT system property not set"))
    private val androidRoot = File(repoRoot, "android")

    private val classDeclRegex = Regex("""\bclass\s+(\w*Request\w*)\b""")

    /** Production Kotlin: `src/main` only, excluding generated (`build/`) and any test source set. */
    private fun productionKotlinFiles(): List<File> {
        val skipDirs = setOf(".git", "build", ".gradle")
        val out = mutableListOf<File>()
        fun walk(dir: File) {
            for (f in dir.listFiles() ?: return) {
                if (f.isDirectory) {
                    if (f.name !in skipDirs) walk(f)
                } else if (f.extension == "kt") {
                    val path = f.path.replace('\\', '/')
                    if ("/src/main/" in path) out.add(f)
                }
            }
        }
        walk(androidRoot)
        return out
    }

    /** Remove block comments, line comments, and string literals so only real code is matched. */
    private fun stripCommentsAndStrings(text: String): String =
        text
            .replace(Regex("""/\*[\s\S]*?\*/"""), " ")
            .replace(Regex("""//[^\n]*"""), " ")
            .replace(Regex(""""{3}[\s\S]*?"{3}"""), "\"\"")
            .replace(Regex(""""(\\.|[^"\\])*""""), "\"\"")

    @Test
    fun `no hand-written production Kotlin source declares a Request-shaped model class`() {
        val offenders = mutableListOf<String>()
        for (file in productionKotlinFiles()) {
            val code = stripCommentsAndStrings(file.readText())
            for (match in classDeclRegex.findAll(code)) {
                offenders.add("${file.relativeTo(repoRoot)}: class ${match.groupValues[1]}")
            }
        }
        assertTrue(offenders.isEmpty(), "hand-rolled request-shaped model(s) found: $offenders")
    }
}
