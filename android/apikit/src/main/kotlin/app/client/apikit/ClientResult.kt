package app.client.apikit

/**
 * SLICE_S7_CONTRACT.md §1e — the total consumer error taxonomy, a single choke point. Every consumer
 * of ApiKit gets exactly one of these four shapes back; there is no fifth "pending" shape (§1e: no
 * pending-chrome component exists for R5/S20 to misuse).
 */
sealed interface ClientResult<out T> {
    data class Ok<T>(val value: T) : ClientResult<T>
    data class Denied(val limitReached: LimitReached?) : ClientResult<Nothing>
    data class Problematic(val messageKey: String) : ClientResult<Nothing>
    data object Offline : ClientResult<Nothing>
}
