package app.client.designkit

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.AddAPhoto
import androidx.compose.material.icons.filled.AlternateEmail
import androidx.compose.material.icons.filled.CalendarToday
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.ConfirmationNumber
import androidx.compose.material.icons.filled.Email
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Explore
import androidx.compose.material.icons.filled.Group
import androidx.compose.material.icons.filled.HourglassEmpty
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Label
import androidx.compose.material.icons.filled.Layers
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.Send
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material.icons.filled.VerifiedUser
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material.icons.filled.WifiOff
import androidx.compose.material.icons.outlined.AccountCircle as AccountCircleOutlined
import androidx.compose.material.icons.outlined.Explore as ExploreOutlined
import androidx.compose.material.icons.outlined.Group as GroupOutlined
import androidx.compose.material.icons.outlined.Inbox as InboxOutlined
import androidx.compose.material.icons.outlined.Layers as LayersOutlined
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp

/**
 * One Iconography layer, per DESIGN.md's Font Awesome Pro spec (SLICE_S7_CONTRACT.md §9a, Correction
 * 2). Call sites never reference a glyph literal or a Material icon directly — they name a semantic
 * [Glyph] and an [IconRegister], and this layer resolves it. Font Awesome Pro is a licensed asset swap
 * behind this seam: when the FA Pro kit is not present in-repo (the common case pre-license-drop),
 * [FallbackGlyphSource] resolves every glyph from the always-bundled Compose Material Icons set, so
 * builds and tests stay green without it. The real FA Pro kit is a DI swap of [Iconography.source]
 * behind this object, never a call-site change.
 *
 * Case names mirror ios/DesignKit/Sources/DesignKit/Iconography.swift's `Glyph` enum 1:1 (the client
 * layering names are shared cross-platform per SLICE_S7_CONTRACT.md §1b) so a reviewer or a future
 * Maestro flow reasons about the same vocabulary on both shells.
 */
enum class Glyph {
    TabConnect,
    TabExplore,
    TabCrews,
    TabInbox,
    TabProfile,
    EmptyDeck,
    EmptyExplore,
    EmptyCrews,
    EmptyInbox,
    EmptyProfile,
    GatePending,
    PresenceFallback,
    BattlePause,
    ConnectivityOffline,
    ContractMismatch,
    DignityShield,
    PreFirstCon,
    SignupHandle,
    SignupEmail,
    SignupBirthdate,
    SignupAvatar,
    SignupFandom,
    LimitReached,
    ProblemGeneric,
    CouldNotSend,
    ChevronForward,
    Checkmark,
}

/** Playful = Solid style; neutral + list rows = Regular style; empty/celebration = Duotone (DESIGN.md). */
enum class IconRegister { Playful, Neutral, EmptyOrCelebration }

fun interface GlyphSource {
    fun resolve(glyph: Glyph, register: IconRegister): ImageVector
}

/**
 * The bundled fallback: Compose Material Icons, always present, zero license dependency, zero egress.
 * Ships in every build until a Julien asset task swaps [Iconography.source] for the real FA Pro-backed
 * [GlyphSource] — a DI swap behind this seam, never a call-site change (Correction 2).
 */
object FallbackGlyphSource : GlyphSource {
    override fun resolve(glyph: Glyph, register: IconRegister): ImageVector {
        val neutral = register == IconRegister.Neutral
        return when (glyph) {
            Glyph.TabConnect -> Icons.Filled.Group
            Glyph.TabExplore -> Icons.Filled.Explore
            Glyph.TabCrews -> Icons.Filled.Group
            Glyph.TabInbox -> Icons.Filled.Inbox
            Glyph.TabProfile -> Icons.Filled.AccountCircle
            Glyph.EmptyDeck -> if (neutral) LayersOutlined else Icons.Filled.Layers
            Glyph.EmptyExplore -> if (neutral) ExploreOutlined else Icons.Filled.Explore
            Glyph.EmptyCrews -> if (neutral) GroupOutlined else Icons.Filled.Group
            Glyph.EmptyInbox -> InboxOutlined
            Glyph.EmptyProfile -> AccountCircleOutlined
            Glyph.GatePending -> Icons.Filled.Schedule
            Glyph.PresenceFallback -> Icons.Filled.Wifi
            Glyph.BattlePause -> Icons.Filled.Pause
            Glyph.ConnectivityOffline -> Icons.Filled.WifiOff
            Glyph.ContractMismatch -> Icons.Filled.Sync
            Glyph.DignityShield -> Icons.Filled.VerifiedUser
            Glyph.PreFirstCon -> Icons.Filled.ConfirmationNumber
            Glyph.SignupHandle -> Icons.Filled.AlternateEmail
            Glyph.SignupEmail -> Icons.Filled.Email
            Glyph.SignupBirthdate -> Icons.Filled.CalendarToday
            Glyph.SignupAvatar -> Icons.Filled.AddAPhoto
            Glyph.SignupFandom -> Icons.Filled.Label
            Glyph.LimitReached -> Icons.Filled.HourglassEmpty
            Glyph.ProblemGeneric -> Icons.Filled.ErrorOutline
            Glyph.CouldNotSend -> Icons.Filled.Send
            Glyph.ChevronForward -> Icons.Filled.KeyboardArrowRight
            Glyph.Checkmark -> Icons.Filled.Check
        }
    }
}

/** The seam. Composables call [Iconography.icon], never a glyph literal or a Material icon directly. */
object Iconography {
    var source: GlyphSource = FallbackGlyphSource

    @Composable
    fun icon(
        glyph: Glyph,
        register: IconRegister = IconRegister.Playful,
        contentDescription: String?,
        modifier: Modifier = Modifier,
        sizeDp: Int = IconTokens.sizes[1],
    ) {
        androidx.compose.material3.Icon(
            imageVector = source.resolve(glyph, register),
            contentDescription = contentDescription,
            modifier = modifier.size(sizeDp.dp),
        )
    }

    /** Minimum-touch-target wrapper (DESIGN.md: 44x44pt minimum), used by icon-only tap targets. */
    @Composable
    fun touchTarget(
        contentDescription: String,
        modifier: Modifier = Modifier,
        content: @Composable () -> Unit,
    ) {
        Box(
            modifier = modifier
                .size(IconTokens.minTouchTargetDp.dp)
                .semantics { this.contentDescription = contentDescription },
        ) {
            content()
        }
    }
}
