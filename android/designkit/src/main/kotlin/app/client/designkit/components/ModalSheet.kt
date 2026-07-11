package app.client.designkit.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import app.client.designkit.Palette
import app.client.designkit.Radii
import app.client.designkit.Spacing
import app.client.designkit.tokenColor

data class ModalAction(val label: String, val onClick: () -> Unit)

/**
 * DESIGN.md Component Anatomy: modal, radius 20, 2px hairline, title 800, body 500, stacked full-width
 * pill buttons, quiet action last. Neutral modals: no outline color, no Bubblegum — enforced here by
 * simply never reading a candy color for the neutral path (no register parameter to misuse).
 */
@Composable
fun ModalSheet(
    title: String,
    body: String,
    primaryAction: ModalAction,
    modifier: Modifier = Modifier,
    quietAction: ModalAction? = null,
) {
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(Radii.modal.dp),
        border = BorderStroke(width = 2.dp, color = tokenColor(Palette.Light.hairline)),
        color = tokenColor(Palette.Light.surface),
    ) {
        Column(
            modifier = Modifier.padding(Spacing.scale[4].dp),
            verticalArrangement = Arrangement.spacedBy(Spacing.scale[2].dp),
        ) {
            Text(text = title)
            Text(text = body)
            Button(onClick = primaryAction.onClick, modifier = Modifier.fillMaxWidth()) {
                Text(text = primaryAction.label)
            }
            if (quietAction != null) {
                TextButton(onClick = quietAction.onClick, modifier = Modifier.fillMaxWidth()) {
                    Text(text = quietAction.label)
                }
            }
        }
    }
}
