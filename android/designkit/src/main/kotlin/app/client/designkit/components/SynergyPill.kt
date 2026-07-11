package app.client.designkit.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import app.client.designkit.Outline
import app.client.designkit.Palette
import app.client.designkit.Radii
import app.client.designkit.Spacing
import app.client.designkit.tokenColor

/**
 * DESIGN.md Component Anatomy: person-card synergy pill — white surface, Choco outline, Sky numeral.
 * DR-6.1 gesture-equivalent slot reservation lands with the deck (S14/S19); this pill is anatomy-only
 * at S7, fixture-driven in the debug gallery. `value` is nullable so a caller can render-absent below
 * the synergy band per DESIGN.md ("synergy pill (render-absent below band)") rather than a disabled state.
 */
@Composable
fun SynergyPill(
    value: String?,
    modifier: Modifier = Modifier,
) {
    if (value == null) return
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(Radii.pill.dp),
        color = tokenColor(Palette.Light.surface),
        border = BorderStroke(width = Outline.chip.dp, color = tokenColor(Palette.Candy.choco)),
    ) {
        Text(
            text = value,
            color = tokenColor(Palette.Candy.sky),
            modifier = Modifier.padding(horizontal = Spacing.scale[1].dp, vertical = Spacing.scale[0].dp),
        )
    }
}
