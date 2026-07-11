package app.client.designkit.components

import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics

/**
 * SLICE_S7_CONTRACT.md law 3 — the SOLE allowlisted exception to "absence, not disablement": the
 * create-crew Premium secondary CTA in the Crews empty state (design/tokens.v1.json
 * `allowlisted_exceptions.create_crew_premium_cta`). This is a live, tappable, honestly-labelled action
 * (it routes to the real upsell surface, never a dead end) — it is NOT a grayed-out/disabled rendering
 * of the primary CTA. No other component in this kit may cite this exception.
 */
@Composable
fun CrewsPremiumCta(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    OutlinedButton(
        onClick = onClick,
        modifier = modifier
            .testTag("crews.create.premium.cta")
            .semantics { contentDescription = label },
    ) {
        Text(text = label)
    }
}
