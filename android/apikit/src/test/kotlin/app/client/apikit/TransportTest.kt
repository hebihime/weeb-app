package app.client.apikit

import kotlin.test.AfterTest
import kotlin.test.BeforeTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer

/**
 * SLICE_S7_CONTRACT.md §1d — exercises [Transport] against a real (local, ephemeral) HTTP server, so
 * the offline / 2xx / 429 paths are proven end to end, not just [ErrorMapper] in isolation. This is the
 * cheapest honest stand-in for "simulator/emulator app against the compose backend" (§12 evidence 3) —
 * an actual E2E run against the real compose backend is a Maestro/CI concern, not a JVM unit test's.
 */
class TransportTest {
    private lateinit var server: MockWebServer

    @BeforeTest
    fun start() {
        server = MockWebServer()
        server.start()
    }

    @AfterTest
    fun stop() {
        server.shutdown()
    }

    @Test
    fun `getClientConfig decodes a real 200 response`() {
        server.enqueue(
            MockResponse().setResponseCode(200).setBody(
                """{"apiVersion":"1.0.0","locales":["en","es","pt","zh-Hans"],"defaultLocale":"en"}""",
            ),
        )
        val transport = Transport(baseUrl = server.url("/").toString().trimEnd('/'))
        val result = transport.getClientConfig()
        val ok = assertIs<ClientResult.Ok<ClientConfigResponse>>(result)
        assertEquals("1.0.0", ok.value.apiVersion)
        assertEquals("en", ok.value.defaultLocale)
    }

    @Test
    fun `getHealth decodes a real 200 response`() {
        server.enqueue(MockResponse().setResponseCode(200).setBody("""{"status":"ok","checkedAt":"2026-01-01T00:00:00Z"}"""))
        val transport = Transport(baseUrl = server.url("/").toString().trimEnd('/'))
        val ok = assertIs<ClientResult.Ok<HealthStatus>>(transport.getHealth())
        assertEquals("ok", ok.value.status)
    }

    @Test
    fun `a real 429 maps to Denied`() {
        server.enqueue(
            MockResponse().setResponseCode(429).setBody(
                """{"quotaKey":"q","messageKey":"limit_reached.generic","resetsAt":"2026-01-01T00:00:00Z","premiumExtends":false}""",
            ),
        )
        val transport = Transport(baseUrl = server.url("/").toString().trimEnd('/'))
        assertIs<ClientResult.Denied>(transport.getClientConfig())
    }

    @Test
    fun `a real 404 maps to the same Problematic shape as a real 403 or 410`() {
        for (status in listOf(403, 404, 410)) {
            server.enqueue(MockResponse().setResponseCode(status).setBody("""{"messageKey":"error.generic"}"""))
        }
        val transport = Transport(baseUrl = server.url("/").toString().trimEnd('/'))
        val results = (1..3).map { transport.getClientConfig() }
        assertEquals(setOf(ClientResult.Problematic(ProblemSurfaceKeys.GENERIC)), results.toSet())
    }

    @Test
    fun `an unreachable server maps to Offline, never a crash`() {
        // A freshly bound-then-released port: nothing is listening there, independent of this test
        // class's shared `server` instance (whose lifecycle stays untouched by @AfterTest either way).
        val deadPort = java.net.ServerSocket(0).use { it.localPort }
        val transport = Transport(baseUrl = "http://127.0.0.1:$deadPort")
        assertIs<ClientResult.Offline>(transport.getClientConfig())
    }
}
