package app.client.designkit.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import app.client.designkit.Glyph
import app.client.designkit.IconRegister
import app.client.designkit.Iconography
import app.client.designkit.Spacing

/** DESIGN.md Component Anatomy: 56px min, leading icon (FA Regular) optional, title + caption, trailing value. */
@Composable
fun ListRow(
    title: String,
    caption: String? = null,
    leadingGlyph: Glyph? = null,
    trailingText: String? = null,
    modifier: Modifier = Modifier,
) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .defaultMinSize(minHeight = 56.dp)
            .padding(PaddingValues(horizontal = Spacing.scale[3].dp)),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Spacing.scale[2].dp),
    ) {
        if (leadingGlyph != null) {
            Iconography.icon(glyph = leadingGlyph, register = IconRegister.Neutral, contentDescription = null)
        }
        Column(modifier = Modifier.padding(vertical = Spacing.scale[1].dp)) {
            Text(text = title)
            if (caption != null) {
                Text(text = caption)
            }
        }
        if (trailingText != null) {
            Text(text = trailingText)
        }
    }
}
