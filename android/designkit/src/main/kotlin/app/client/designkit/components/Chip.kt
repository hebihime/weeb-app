package app.client.designkit.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import app.client.designkit.Palette
import app.client.designkit.Radii
import app.client.designkit.Register
import app.client.designkit.Spacing
import app.client.designkit.tokenColor

/**
 * DESIGN.md Component Anatomy: pill, 2px outline (Choco in playful, hairline in neutral), 700 text.
 * No "selected but disabled" or "locked" variant exists — [selected] is the only state this type
 * admits (law 3: absence, not disablement).
 */
@Composable
fun Chip(
    label: String,
    selected: Boolean,
    register: Register,
    modifier: Modifier = Modifier,
) {
    val outlineColor = if (register == Register.Playful) tokenColor(Palette.Candy.choco) else tokenColor(Palette.Light.hairline)
    val fillColor = when {
        selected && register == Register.Playful -> tokenColor(Palette.Candy.bubblegum)
        else -> tokenColor(Palette.Light.surface)
    }
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(Radii.pill.dp),
        color = fillColor,
        border = BorderStroke(width = 2.dp, color = outlineColor),
    ) {
        Text(
            text = label,
            modifier = Modifier.padding(horizontal = Spacing.scale[2].dp, vertical = Spacing.scale[1].dp),
        )
    }
}
