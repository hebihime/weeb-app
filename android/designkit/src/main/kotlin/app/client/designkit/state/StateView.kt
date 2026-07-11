package app.client.designkit.state

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import app.client.designkit.Glyph
import app.client.designkit.IconRegister
import app.client.designkit.Iconography
import app.client.designkit.Spacing

/**
 * SLICE_S7_CONTRACT.md §9d — ONE StateView for the whole 23-state catalog: illustration slot [FA
 * duotone], title 800, body 500, single CTA slot. No "pending…" indicator variant exists (§1e — the
 * leak-shaped component the deny/void class never gets). No disabled/locked visual variant exists
 * either (law 3) — [ctaLabel]/[onCta] are either both present (a live action) or both absent (no
 * action), never a half-present "disabled-looking" button.
 *
 * Every empty state routes to a live action, never a dead end (§9b) — callers that have nowhere to
 * route yet (S7's honest empties) still pass a CTA that does something real (e.g. "Explore" jumps to
 * the Explore tab), never a decorative disabled button standing in for one.
 */
@Composable
fun StateView(
    title: String,
    body: String,
    glyph: Glyph,
    register: Register,
    modifier: Modifier = Modifier,
    testTag: String? = null,
    ctaLabel: String? = null,
    onCta: (() -> Unit)? = null,
    ctaTestTag: String? = null,
) {
    val iconRegister = if (register == Register.Playful) IconRegister.EmptyOrCelebration else IconRegister.Neutral
    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(Spacing.scale[4].dp)
            .then(if (testTag != null) Modifier.testTag(testTag).semantics { contentDescription = title } else Modifier),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(Spacing.scale[2].dp),
    ) {
        Iconography.icon(glyph = glyph, register = iconRegister, contentDescription = null, sizeDp = 48)
        Text(text = title)
        Text(text = body)
        if (ctaLabel != null && onCta != null) {
            Button(
                onClick = onCta,
                modifier = if (ctaTestTag != null) {
                    Modifier.testTag(ctaTestTag).semantics { contentDescription = ctaLabel }
                } else {
                    Modifier
                },
            ) { Text(text = ctaLabel) }
        }
    }
}
