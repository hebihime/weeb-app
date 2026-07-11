package app.client.apikit

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

class ErrorMapperTest {
    private fun decodeConfig(s: String) = ErrorMapper.tolerantJson.decodeFromString<ClientConfigResponse>(s)

    @Test
    fun `2xx decodes into Ok`() {
        val body = """{"apiVersion":"1.0.0","locales":["en","es","pt","zh-Hans"],"defaultLocale":"en"}"""
        val result = ErrorMapper.map(200, body, ::decodeConfig)
        val ok = assertIs<ClientResult.Ok<ClientConfigResponse>>(result)
        assertEquals("1.0.0", ok.value.apiVersion)
    }

    @Test
    fun `429 maps to the one LimitReached surface`() {
        val body = """{"quotaKey":"q","messageKey":"limit_reached.generic","resetsAt":"2026-01-01T00:00:00Z","premiumExtends":false}"""
        val result = ErrorMapper.map(429, body, ::decodeConfig)
        val denied = assertIs<ClientResult.Denied>(result)
        assertEquals("limit_reached.generic", denied.limitReached?.messageKey)
    }

    @Test
    fun `429 with an undecodable body still denies, never crashes`() {
        val result = ErrorMapper.map(429, "not json", ::decodeConfig)
        assertIs<ClientResult.Denied>(result)
    }

    @Test
    fun `every other status maps to the one generic Problem surface`() {
        for (status in listOf(400, 401, 500, 502, 503)) {
            val result = ErrorMapper.map(status, """{"messageKey":"whatever"}""", ::decodeConfig)
            val problem = assertIs<ClientResult.Problematic>(result)
            assertEquals(ProblemSurfaceKeys.GENERIC, problem.messageKey)
        }
    }

    @Test
    fun `404-uniformity - 403, 404 and 410 are byte-identical, zero distinction`() {
        // contract-lint already bans 403 on consumer reads server-side; this is the client's second
        // oracle (§9f L20) — the mapper must be INCAPABLE of leaking which of the three it saw.
        val bodies = listOf(
            """{"type":"about:blank","title":"Forbidden","messageKey":"error.generic","correlationId":"corr-403"}""",
            """{"type":"about:blank","title":"Not Found","messageKey":"error.generic","correlationId":"corr-404"}""",
            """{"type":"about:blank","title":"Gone","messageKey":"error.generic","correlationId":"corr-410"}""",
        )
        val results = listOf(403, 404, 410).zip(bodies).map { (status, body) -> ErrorMapper.map(status, body, ::decodeConfig) }
        val distinct = results.toSet()
        assertEquals(1, distinct.size, "403/404/410 produced distinguishable ClientResults: $results")
        assertEquals(ClientResult.Problematic(ProblemSurfaceKeys.GENERIC), results[0])
    }

    @Test
    fun `a missing body on a 2xx never crashes and falls back to the generic problem surface`() {
        val result = ErrorMapper.map(204, null, ::decodeConfig)
        assertIs<ClientResult.Problematic>(result)
    }
}
