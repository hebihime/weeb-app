package app.client.designkit.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.unit.dp
import app.client.designkit.Outline
import app.client.designkit.Palette
import app.client.designkit.Radii
import app.client.designkit.Spacing
import app.client.designkit.tokenColor

/**
 * DESIGN.md Component Anatomy: person card (deck) — full-bleed photo slot, 3px Choco outline, radius
 * 24, synergy pill top-right, status chip top-left, bottom scrim with name + meta. Anatomy only at S7
 * (fixture-driven in the debug gallery, §9a) — the real deck ships with S14/S19. `photo` is a slot: this
 * frame renders whatever the caller passes (an illustration IS the AI-character disclosure per
 * DESIGN.md — no extra badge needed), never a live person's data at S7 (L6: zero fabricated data).
 */
@Composable
fun PersonCardFrame(
    name: String,
    metaLine: String,
    modifier: Modifier = Modifier,
    statusChip: (@Composable () -> Unit)? = null,
    synergyPill: (@Composable () -> Unit)? = null,
    photo: @Composable () -> Unit,
) {
    BoxWithConstraints(
        modifier = modifier
            .clip(RoundedCornerShape(Radii.card.dp))
            .background(tokenColor(Palette.Light.surface2)),
    ) {
        Surface(
            modifier = Modifier,
            shape = RoundedCornerShape(Radii.card.dp),
            border = BorderStroke(width = Outline.personCard.dp, color = tokenColor(Palette.Candy.choco)),
            color = tokenColor(Palette.Light.surface2),
        ) {
            Box {
                photo()
                if (statusChip != null) {
                    Box(modifier = Modifier.align(Alignment.TopStart).padding(Spacing.scale[2].dp)) { statusChip() }
                }
                if (synergyPill != null) {
                    Box(modifier = Modifier.align(Alignment.TopEnd).padding(Spacing.scale[2].dp)) { synergyPill() }
                }
                Column(
                    modifier = Modifier
                        .align(Alignment.BottomStart)
                        .padding(Spacing.scale[3].dp),
                ) {
                    Text(text = name, color = tokenColor(Palette.Light.ground))
                    Text(text = metaLine, color = tokenColor(Palette.Light.ground))
                }
            }
        }
    }
}
