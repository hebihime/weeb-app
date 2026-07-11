package app.client.designkit.components

import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import app.client.designkit.Palette
import app.client.designkit.Radii
import app.client.designkit.Spacing
import app.client.designkit.tokenColor

enum class BadgeStyle { Standard, Foil }

/**
 * DESIGN.md Component Anatomy: pill, micro-label type; Foil variant for earned only. Law 1 (foil never
 * ranks people) is enforced at the call site, not here — this component has no notion of "a person's
 * card", only a style enum with exactly two cases, neither of which is a rank.
 */
@Composable
fun Badge(
    label: String,
    style: BadgeStyle,
    modifier: Modifier = Modifier,
) {
    val color = if (style == BadgeStyle.Foil) tokenColor(Palette.Candy.foil) else tokenColor(Palette.Light.dim)
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(Radii.pill.dp),
        color = tokenColor(Palette.Light.surface2),
    ) {
        Text(
            text = label,
            color = color,
            modifier = Modifier.padding(horizontal = Spacing.scale[1].dp, vertical = Spacing.scale[0].dp),
        )
    }
}
