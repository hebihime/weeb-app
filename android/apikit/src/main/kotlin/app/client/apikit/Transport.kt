package app.client.apikit

import java.io.IOException
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * SLICE_S7_CONTRACT.md §1d — the ONLY consumed surface: `GET /health` (debug diagnostics only) and
 * `GET /v1/client-config`. [baseUrl] is supplied by the caller (debug-build-only diagnostics screen);
 * a release build never constructs a [Transport] because nothing in `src/main`/`src/release` holds a
 * backend URL to pass one with (§9f, the release-config test in `:app` asserts this by absence).
 *
 * No client-config cache, no TTL store — every call is a fresh network round-trip (§1d: a cache would
 * violate the §3 zero-persistence inventory).
 *
 * The OkHttp client is created and held entirely internally (never a constructor parameter): OkHttp is
 * an `implementation`-scoped dependency of this module on purpose (§9f — consumers reach the network
 * ONLY through this one seam), so no OkHttp type is part of Transport's public surface for a consuming
 * module (e.g. `:app`'s debug-only diagnostics screen) to need on its own compile classpath.
 */
class Transport(private val baseUrl: String) {
    private val client: OkHttpClient = OkHttpClient()

    fun getHealth(): ClientResult<HealthStatus> = get("$baseUrl/health") {
        ErrorMapper.tolerantJson.decodeFromString<HealthStatus>(it)
    }

    fun getClientConfig(): ClientResult<ClientConfigResponse> = get("$baseUrl/v1/client-config") {
        ErrorMapper.tolerantJson.decodeFromString<ClientConfigResponse>(it)
    }

    private fun <T> get(url: String, decode: (String) -> T): ClientResult<T> {
        val request = Request.Builder().url(url).get().build()
        return try {
            client.newCall(request).execute().use { response ->
                val body = response.body?.string()
                ErrorMapper.map(response.code, body, decode)
            }
        } catch (e: IOException) {
            ClientResult.Offline
        }
    }
}
