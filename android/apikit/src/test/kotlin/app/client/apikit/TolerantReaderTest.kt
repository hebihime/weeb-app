package app.client.apikit

import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * SLICE_S7_CONTRACT.md §1d — "Tolerant readers, asserted: both generated-client wrappers ignore unknown
 * response fields (test with an extended fixture payload)." Named beneficiary: a future additive
 * `min_supported_client_version` on `ClientConfigResponse` (force-upgrade, S26) must not break this
 * client the day the server starts sending it.
 */
class TolerantReaderTest {
    @Test
    fun `ClientConfigResponse decodes fine with an unknown extra field`() {
        val extended = """
            {
              "apiVersion": "1.0.0",
              "locales": ["en", "es", "pt", "zh-Hans"],
              "defaultLocale": "en",
              "minSupportedClientVersion": "9.9.9"
            }
        """.trimIndent()
        val decoded = ErrorMapper.tolerantJson.decodeFromString<ClientConfigResponse>(extended)
        assertEquals("1.0.0", decoded.apiVersion)
        assertEquals(listOf("en", "es", "pt", "zh-Hans"), decoded.locales)
        assertEquals("en", decoded.defaultLocale)
    }

    @Test
    fun `HealthStatus decodes fine with an unknown extra field`() {
        val extended = """{"status":"ok","checkedAt":"2026-01-01T00:00:00Z","region":"eu-west"}"""
        val decoded = ErrorMapper.tolerantJson.decodeFromString<HealthStatus>(extended)
        assertEquals("ok", decoded.status)
    }

    @Test
    fun `Problem decodes fine with the numeric status field it does not model`() {
        val withStatus = """
            {"type":"about:blank","title":"Bad","status":400,"messageKey":"error.generic","correlationId":"c1"}
        """.trimIndent()
        val decoded = ErrorMapper.tolerantJson.decodeFromString<Problem>(withStatus)
        assertEquals("error.generic", decoded.messageKey)
    }

    @Test
    fun `Problem decodes fine with the string-shaped status field (openapi 3_1 union type)`() {
        val withStringStatus = """
            {"type":"about:blank","title":"Bad","status":"400","messageKey":"error.generic","correlationId":"c1"}
        """.trimIndent()
        val decoded = ErrorMapper.tolerantJson.decodeFromString<Problem>(withStringStatus)
        assertEquals("error.generic", decoded.messageKey)
    }
}
