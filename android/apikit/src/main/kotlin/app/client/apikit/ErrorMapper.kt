package app.client.apikit

import kotlinx.serialization.json.Json

/**
 * SLICE_S7_CONTRACT.md §1e — the ONE error mapper. 2xx renders the decoded body; 429 maps to the ONE
 * LimitReached surface (`limit_reached.generic`); every other status maps to the ONE generic Problem
 * surface (`error.generic`) — deliberately WITHOUT branching on which status it was. That deliberate
 * absence of a switch is what makes the 404-uniformity test pass by construction: 403/404/410 (or any
 * other non-2xx, non-429 status) all fall into the same `else`, so the mapped [ClientResult] is
 * structurally identical no matter which of those statuses the server actually returned — the mapper
 * is INCAPABLE of leaking the distinction, not merely tested not to leak it (contract-lint already bans
 * 403 on consumer reads server-side; this is the client's second oracle, §9f L20).
 *
 * Transport-offline (no HTTP response at all) is a distinct case the mapper never sees — [Transport]
 * returns [ClientResult.Offline] directly from its `catch (IOException)`, never calling [map].
 */
object ErrorMapper {
    val tolerantJson: Json = Json { ignoreUnknownKeys = true }

    fun <T> map(statusCode: Int, body: String?, decode: (String) -> T): ClientResult<T> {
        return when {
            statusCode in 200..299 -> {
                val decoded = body?.let { runCatching { decode(it) }.getOrNull() }
                if (decoded != null) ClientResult.Ok(decoded) else ClientResult.Problematic(ProblemSurfaceKeys.GENERIC)
            }
            statusCode == 429 -> {
                val limitReached = body?.let {
                    runCatching { tolerantJson.decodeFromString<LimitReached>(it) }.getOrNull()
                }
                ClientResult.Denied(limitReached)
            }
            // Every other status — 400, 401, 403, 404, 410, 422, 500, 502, 503, anything else — maps
            // here, on purpose, without reading `statusCode` again below this line. That is the whole
            // proof: this branch cannot distinguish 403 from 404 from 410 because it never looks.
            else -> ClientResult.Problematic(ProblemSurfaceKeys.GENERIC)
        }
    }
}

/** Message-key constants — never a raw string literal at a call site (§1e / contracts/message-keys.json). */
object ProblemSurfaceKeys {
    const val GENERIC = "error.generic"
    const val COULD_NOT_SEND = "error.could_not_send"
    const val LIMIT_REACHED_GENERIC = "limit_reached.generic"
}
