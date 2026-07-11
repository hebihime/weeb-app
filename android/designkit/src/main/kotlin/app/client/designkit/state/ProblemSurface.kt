package app.client.designkit.state

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import app.client.designkit.Glyph

/**
 * SLICE_S7_CONTRACT.md §1e / token law 2 — THE ONE generic Problem surface. Every non-2xx, non-429
 * status the error mapper sees renders here, neutral register, danger never reaching the playful
 * register. `error.could_not_send` (the signup gateway's honest refusal) is this SAME surface bound to
 * a different message key, not a second component — [render] takes the key explicitly so both bindings
 * share one implementation and one test can assert both.
 */
object ProblemSurface {
    const val GENERIC_MESSAGE_KEY: String = "error.generic"
    const val COULD_NOT_SEND_MESSAGE_KEY: String = "error.could_not_send"

    @Composable
    fun render(
        title: String,
        body: String,
        messageKey: String,
        modifier: Modifier = Modifier,
        ctaLabel: String? = null,
        onCta: (() -> Unit)? = null,
    ) {
        val testTag = when (messageKey) {
            COULD_NOT_SEND_MESSAGE_KEY -> "state.error.could_not_send"
            else -> "state.error.generic"
        }
        StateView(
            title = title,
            body = body,
            glyph = if (messageKey == COULD_NOT_SEND_MESSAGE_KEY) Glyph.CouldNotSend else Glyph.ProblemGeneric,
            register = Register.Neutral,
            modifier = modifier,
            testTag = testTag,
            ctaLabel = ctaLabel,
            onCta = onCta,
        )
    }
}
