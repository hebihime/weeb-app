package app.client.apikit

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §9f — "zero non-generated request models exist in ApiKit consumers (lint)."
 * S7 ships zero request bodies (both consumed operations are bodiless GETs, §1d), so the true
 * statement is stronger than "none outside the generated tree" — none should exist anywhere in
 * hand-written android/ source at all. A future slice that legitimately needs one gets it from
 * generated code (openApiGenerate), never a hand-typed `data class FooRequest`.
 */
class NoHandRolledRequestModelTest {
    private val repoRoot = File(System.getProperty("REPO_ROOT") ?: error("REPO_ROOT system property not set"))
    private val androidRoot = File(repoRoot, "android")

    private val classDeclRegex = Regex("""class\s+(\w*Request\w*)\b""")

    private fun handWrittenKotlinFiles(): List<File> {
        val skipDirs = setOf(".git", "build", ".gradle")
        val out = mutableListOf<File>()
        fun walk(dir: File) {
            val entries = dir.listFiles() ?: return
            for (f in entries) {
                if (f.isDirectory) {
                    if (f.name !in skipDirs) walk(f)
                } else if (f.extension == "kt") {
                    out.add(f)
                }
            }
        }
        walk(androidRoot)
        return out
    }

    @Test
    fun `no hand-written Kotlin source declares a Request-shaped model class`() {
        val offenders = mutableListOf<String>()
        for (file in handWrittenKotlinFiles()) {
            val text = file.readText()
            for (match in classDeclRegex.findAll(text)) {
                offenders.add("${file.relativeTo(repoRoot)}: class ${match.groupValues[1]}")
            }
        }
        assertTrue(offenders.isEmpty(), "hand-rolled request-shaped model(s) found: $offenders")
    }
}
