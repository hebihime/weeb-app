package app.client

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §9f — "a Play Data Safety source file ... gate-tested against the §3 inventory
 * so declaration drift = red build." §3's device-local data inventory is exhaustive and empty (no PII,
 * nothing written); this test is the truth-check that keeps the declaration honest.
 */
class PlayDataSafetyTest {
    private val repoRoot = File(System.getProperty("REPO_ROOT") ?: error("REPO_ROOT system property not set"))

    private fun declaration() = Json.parseToJsonElement(
        File(repoRoot, "android/app/datasafety/play_data_safety.json").readText(),
    ).jsonObject

    @Test
    fun `tracking is false`() {
        val doc = declaration()
        assertEquals(false, doc["tracking"]!!.jsonPrimitive.boolean)
    }

    @Test
    fun `independentSecurityReview is honestly false (nothing reviewed yet)`() {
        assertEquals(false, declaration()["independentSecurityReview"]!!.jsonPrimitive.boolean)
    }

    @Test
    fun `dataCollected is empty`() {
        assertTrue(declaration()["dataCollected"]!!.jsonArray.isEmpty())
    }

    @Test
    fun `dataShared is empty`() {
        assertTrue(declaration()["dataShared"]!!.jsonArray.isEmpty())
    }

    @Test
    fun `data is declared encrypted in transit`() {
        val practices = declaration()["securityPractices"]!!.jsonObject
        assertEquals(true, practices["dataEncryptedInTransit"]!!.jsonPrimitive.boolean)
    }
}
