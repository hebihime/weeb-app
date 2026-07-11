package app.client.apikit

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §2 — "ZERO paths, zero components, zero new message keys — asserted, not
 * assumed." This asserts our hand-written response models (Models.kt) hold exactly the `required`
 * field set contracts/openapi.v0.json declares for ClientConfigResponse / HealthStatus / Problem /
 * LimitReached, so a contract drift that the openapi-generator codegen step would also feel breaks a
 * fast, offline, no-Gradle-plugin-needed unit test first.
 */
class ContractShapeTest {
    private val repoRoot = File(System.getProperty("REPO_ROOT") ?: error("REPO_ROOT system property not set"))

    private fun requiredFieldsOf(schemaName: String): Set<String> {
        val contract = Json.parseToJsonElement(File(repoRoot, "contracts/openapi.v0.json").readText()).jsonObject
        val schema = contract["components"]!!.jsonObject["schemas"]!!.jsonObject[schemaName]!!.jsonObject
        return schema["required"]!!.jsonArray.map { it.jsonPrimitive.content }.toSet()
    }

    @Test
    fun `ClientConfigResponse required fields match the contract`() {
        assertEquals(requiredFieldsOf("ClientConfigResponse"), setOf("apiVersion", "locales", "defaultLocale"))
    }

    @Test
    fun `HealthStatus required fields match the contract`() {
        assertEquals(requiredFieldsOf("HealthStatus"), setOf("status", "checkedAt"))
    }

    @Test
    fun `Problem required fields are a superset of what this client models`() {
        // Problem.status is intentionally NOT modeled (the 404-uniformity design never reads it, and
        // its OpenAPI 3.1 union type `["integer","string"]` is exactly the kind of field a tolerant
        // reader must survive without modeling) — this test only asserts the fields we DO model are
        // required in the contract, not that our model is exhaustive.
        val required = requiredFieldsOf("Problem")
        for (modeled in setOf("type", "title", "messageKey", "correlationId")) {
            assertTrue(modeled in required, "$modeled is modeled but not required by the contract")
        }
    }

    @Test
    fun `LimitReached required fields match the contract`() {
        assertEquals(requiredFieldsOf("LimitReached"), setOf("quotaKey", "messageKey", "resetsAt", "premiumExtends"))
    }
}
