package app.client.designkit

/**
 * The Android mirror of design/tokens.v1.json (SLICE_S7_CONTRACT.md §9a). Hand-written, values only
 * from the manifest — verified value-for-value by tools/token-lint. DESIGN.md is the authoritative
 * prose; this file and the manifest are its machine-readable copies.
 *
 * Every palette hex the manifest declares (candy + semantic + light + dark) must appear below as a
 * string literal, and no hex outside that set may appear anywhere in this file. Brand-delta colors
 * (brand.primary / brand.celebration) are NOT palette values — they come from the active build
 * flavor's brand.properties / resValue, never a literal here or anywhere else in the client trees.
 */
object Palette {
    /** The mark's world: saturated candy colors. Shared by both brands; brand delta layers on top. */
    object Candy {
        const val bubblegum = "#F7568F"
        const val sky = "#38BDF2"
        const val mikan = "#FF9838"
        const val foil = "#C99A2E"
        const val choco = "#1E1410"
    }

    object Semantic {
        const val good = "#3FB950"
        const val warn = "#F5A623"
        const val danger = "#ED4245"
    }

    /** Light mode is the only mode rendered at S7 (apps force light mode; DR-6.3). */
    object Light {
        const val ground = "#FFFFFF"
        const val surface = "#FFFFFF"
        const val surface2 = "#F7F5F2"
        const val hairline = "#E8E3DD"
        const val text = "#26170F"
        const val dim = "#8A7C72"
        const val outline = "#2B1B12"
    }

    /**
     * Choco dark palette. Carried from the start per DR-6.3 but rendered nowhere until its G3 slice
     * — force-light-mode at S7 means nothing in this object is ever read by a live composable yet.
     */
    object Dark {
        const val ground = "#1E1410"
        const val surface = "#2A1D16"
        const val surface2 = "#35251D"
        const val line = "#48362B"
        const val text = "#FBF3EC"
        const val dim = "#B3A093"
        const val outline = "#F5EBE2"
    }
}

/** Mobile type scale (DESIGN.md "Scale (mobile)" table). Sizes/line-heights in sp. */
object TypeScale {
    data class Role(val size: Int, val line: Int, val weight: Int)

    val displayXl = Role(size = 34, line = 40, weight = 900)
    val display = Role(size = 28, line = 34, weight = 900)
    val title = Role(size = 22, line = 28, weight = 800)
    val heading = Role(size = 17, line = 24, weight = 800)
    val body = Role(size = 15, line = 22, weight = 500)
    val caption = Role(size = 13, line = 18, weight = 500)
    val microLabel = Role(size = 11, line = 14, weight = 700)

    /** Neutral register: 400/500/700 only. Black 900 never appears on safety surfaces (law 2 kin). */
    val neutralAllowedWeights = setOf(400, 500, 700)
}

/** 4px base unit, comfortable density. */
object Spacing {
    const val base = 4
    val scale = listOf(4, 8, 12, 16, 24, 32, 48, 64)
}

/** Border radii in dp. Neutral register caps at 16 and drops the chocolate outline entirely. */
object Radii {
    const val card = 24
    const val modal = 20
    const val sheet = 20
    const val input = 16
    const val pill = 999
    const val neutralMax = 16
}

/** Sticker outline width in dp. Playful-register key objects only (person card, chips, badges). */
object Outline {
    const val playfulMin = 2
    const val playfulMax = 3
    const val personCard = 3
    const val chip = 2
    const val crewCard = 3
    const val neutral = 0
}

/** Motion durations in milliseconds. `prefers-reduced-motion` degrades every entry to a static frame. */
object Motion {
    val microMs = 80..120
    val shortMs = 150..250
    val mediumMs = 250..400
    val celebrationMs = 400..700
}

/** Font Awesome Pro sizes (dp) + the minimum touch target the Iconography seam enforces (Correction 2). */
object IconTokens {
    val sizes = listOf(16, 20, 24)
    const val minTouchTargetDp = 44
}
