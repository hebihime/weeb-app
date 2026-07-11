package app.client.designkit.state

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import app.client.designkit.Glyph

/**
 * SLICE_S7_CONTRACT.md §1e / §6 / token law 4 — THE ONE deny surface. Freemium caps and
 * reputation-scaled caps render identically; there is no second LimitReached-shaped component anywhere
 * in the kit. [MESSAGE_KEY] is the exact contracts/message-keys.json binding a test asserts against —
 * this composable can never quietly drift onto a different key.
 */
object LimitReachedSurface {
    const val MESSAGE_KEY: String = "limit_reached.generic"

    @Composable
    fun render(
        title: String,
        body: String,
        modifier: Modifier = Modifier,
        ctaLabel: String? = null,
        onCta: (() -> Unit)? = null,
    ) {
        StateView(
            title = title,
            body = body,
            glyph = Glyph.LimitReached,
            register = Register.Neutral,
            modifier = modifier,
            testTag = "state.limit_reached.generic",
            ctaLabel = ctaLabel,
            onCta = onCta,
        )
    }
}
