package app.client.designkit

import android.content.Context
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
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import org.json.JSONObject
import java.io.IOException

/**
 * One Iconography layer, per DESIGN.md's Font Awesome Pro spec (SLICE_S7_CONTRACT.md §9a, Correction
 * 2). Call sites never reference a glyph literal or a Material icon directly — they name a semantic
 * [Glyph] and an [IconRegister], and this layer resolves it. Font Awesome Pro is a licensed asset swap
 * behind this seam: the FA Pro .otf faces are gitignored (paid license, public repo), so they are
 * present on a licensed dev box and ABSENT on CI. This layer detects that at RUNTIME —
 * [FontAwesomeAssets.build] probes the bundled assets and returns an FA-backed [GlyphSource] only when
 * both faces AND the codepoint map are present; otherwise [FallbackGlyphSource] resolves every glyph
 * from the always-bundled Compose Material Icons set, so builds and tests stay green without the fonts.
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

/**
 * What a [GlyphSource] resolves a glyph to. Two backends, one render seam:
 * - [Vector] — a Compose [ImageVector] (the Material-icons fallback), rendered via material3 `Icon`.
 * - [FaGlyph] — a Font Awesome font face + a PUA codepoint, rendered as text in that face.
 */
sealed interface GlyphRender {
    data class Vector(val image: ImageVector) : GlyphRender
    data class FaGlyph(val fontFamily: FontFamily, val codepoint: Int) : GlyphRender
}

fun interface GlyphSource {
    fun resolve(glyph: Glyph, register: IconRegister): GlyphRender
}

/**
 * The bundled fallback: Compose Material Icons, always present, zero license dependency, zero egress.
 * Used whenever the FA Pro faces are not bundled (the CI reality — the .otf are gitignored).
 */
object FallbackGlyphSource : GlyphSource {
    // [register] is part of the GlyphSource contract (the real FA Pro-backed source picks Solid/Regular
    // by register); the bundled Material-icons fallback maps every glyph to a single Filled variant that
    // is guaranteed to exist — style fidelity arrives with the FA Pro swap (Correction 2). We
    // deliberately avoid Icons.Outlined.* aliases: those are extension properties that cannot be
    // referenced without their `Icons.Outlined` receiver.
    override fun resolve(glyph: Glyph, register: IconRegister): GlyphRender {
        val image = when (glyph) {
            Glyph.TabConnect -> Icons.Filled.Group
            Glyph.TabExplore -> Icons.Filled.Explore
            Glyph.TabCrews -> Icons.Filled.Group
            Glyph.TabInbox -> Icons.Filled.Inbox
            Glyph.TabProfile -> Icons.Filled.AccountCircle
            Glyph.EmptyDeck -> Icons.Filled.Layers
            Glyph.EmptyExplore -> Icons.Filled.Explore
            Glyph.EmptyCrews -> Icons.Filled.Group
            Glyph.EmptyInbox -> Icons.Filled.Inbox
            Glyph.EmptyProfile -> Icons.Filled.AccountCircle
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
        return GlyphRender.Vector(image)
    }
}

/**
 * The real FA Pro-backed source: one PUA codepoint per glyph, rendered in the Solid face (playful +
 * empty/celebration) or the Regular face (neutral + list rows). Duotone (a two-layer render) is
 * deferred; until then empty/celebration uses the Solid face at the mapped codepoint (DESIGN.md).
 */
class FontAwesomeGlyphSource(
    private val solid: FontFamily,
    private val regular: FontFamily,
    private val codepoints: Map<Glyph, Int>,
) : GlyphSource {
    override fun resolve(glyph: Glyph, register: IconRegister): GlyphRender {
        val family = if (register == IconRegister.Neutral) regular else solid
        val codepoint = codepoints[glyph] ?: error("no FA codepoint mapped for $glyph")
        return GlyphRender.FaGlyph(fontFamily = family, codepoint = codepoint)
    }
}

/**
 * Runtime detection + loading of the FA Pro assets bundled at `assets/fa-glyph-map.json` and
 * the two `.otf` faces under `assets/fonts/`. The `.otf` are gitignored, so this is the mechanism that keeps the build green
 * whether or not a licensed box dropped the fonts in.
 */
internal object FontAwesomeAssets {
    const val MAP_ASSET = "fa-glyph-map.json"
    const val SOLID_FONT = "fonts/fontawesome7pro_solid_900.otf"
    const val REGULAR_FONT = "fonts/fontawesome7pro_regular_400.otf"

    /**
     * PURE parser: canonical map JSON text -> `Glyph -> codepoint`. Uses `org.json` (Android built-in,
     * no dependency). The JSON keys are camelCase (`tabConnect`); the [Glyph] enum is PascalCase
     * (`TabConnect`) — matched case-insensitively. Each glyph's `unicode` is hex (e.g. `f0c0`, `40`).
     */
    fun parseCodepoints(json: String): Map<Glyph, Int> {
        val glyphs = JSONObject(json).getJSONObject("glyphs")
        val byLowerName = Glyph.entries.associateBy { it.name.lowercase() }
        val out = LinkedHashMap<Glyph, Int>()
        val keys = glyphs.keys()
        while (keys.hasNext()) {
            val key = keys.next()
            val glyph = byLowerName[key.lowercase()]
                ?: error("fa-glyph-map.json glyph key '$key' has no matching Glyph enum entry")
            val unicode = glyphs.getJSONObject(key).getString("unicode")
            out[glyph] = unicode.toInt(16)
        }
        return out
    }

    /** True only if BOTH faces open cleanly. A missing `.otf` throws [IOException] -> FA unavailable. */
    private fun fontsPresent(context: Context): Boolean {
        return try {
            context.assets.open(SOLID_FONT).close()
            context.assets.open(REGULAR_FONT).close()
            true
        } catch (_: IOException) {
            false
        }
    }

    private fun readMap(context: Context): String? {
        return try {
            context.assets.open(MAP_ASSET).use { it.readBytes().toString(Charsets.UTF_8) }
        } catch (_: IOException) {
            null
        }
    }

    /**
     * Build the FA source, or null when FA is unavailable (either `.otf` absent, the map absent, the map
     * unparseable, or the map does not cover every [Glyph]). Presence is probed BEFORE the font family
     * is built so a missing face never reaches Compose's font loader.
     */
    fun build(context: Context): FontAwesomeGlyphSource? {
        if (!fontsPresent(context)) return null
        val json = readMap(context) ?: return null
        val codepoints = try {
            parseCodepoints(json)
        } catch (_: Exception) {
            return null
        }
        if (!codepoints.keys.containsAll(Glyph.entries.toSet())) return null
        val assets = context.assets
        val solid = FontFamily(Font(path = SOLID_FONT, assetManager = assets))
        val regular = FontFamily(Font(path = REGULAR_FONT, assetManager = assets))
        return FontAwesomeGlyphSource(solid = solid, regular = regular, codepoints = codepoints)
    }
}

/**
 * Env-independent PURE chooser: the FA source if the runtime probe produced one, else the always-green
 * [FallbackGlyphSource]. Kept pure (no Context, no I/O) so it is unit-testable without a device.
 */
fun chooseSource(fa: FontAwesomeGlyphSource?): GlyphSource = fa ?: FallbackGlyphSource

/** Process-wide cache of the runtime FA probe: resolved once per process, then reused. */
private object FaSourceCache {
    @Volatile
    private var resolved = false

    @Volatile
    private var cached: FontAwesomeGlyphSource? = null

    fun get(context: Context): FontAwesomeGlyphSource? {
        if (resolved) return cached
        synchronized(this) {
            if (!resolved) {
                cached = FontAwesomeAssets.build(context.applicationContext)
                resolved = true
            }
        }
        return cached
    }
}

/** The seam. Composables call [Iconography.icon], never a glyph literal or a Material icon directly. */
object Iconography {
    // Effective-source precedence in [icon] (documented so a future reader does not "fix" it):
    //   1. An EXPLICIT override of [source] (any assignment) always wins — for tests and a manual DI
    //      swap. The default-value initializer below does NOT go through the setter, so the override
    //      flag stays false until someone actually assigns.
    //   2. Otherwise: the process-cached Font Awesome source derived from the Android asset [Context],
    //      when the `.otf` faces + the codepoint map are all present at runtime (chooseSource).
    //   3. Otherwise: [FallbackGlyphSource] (Material Icons), the always-green default.
    private var overridden = false

    var source: GlyphSource = FallbackGlyphSource
        set(value) {
            field = value
            overridden = true
        }

    @Composable
    fun icon(
        glyph: Glyph,
        register: IconRegister = IconRegister.Playful,
        contentDescription: String?,
        modifier: Modifier = Modifier,
        sizeDp: Int = IconTokens.sizes[1],
    ) {
        val context = LocalContext.current
        val effective: GlyphSource = if (overridden) source else chooseSource(FaSourceCache.get(context))
        when (val render = effective.resolve(glyph, register)) {
            is GlyphRender.Vector ->
                androidx.compose.material3.Icon(
                    imageVector = render.image,
                    contentDescription = contentDescription,
                    modifier = modifier.size(sizeDp.dp),
                )

            is GlyphRender.FaGlyph ->
                androidx.compose.material3.Text(
                    text = String(Character.toChars(render.codepoint)),
                    fontFamily = render.fontFamily,
                    fontSize = with(LocalDensity.current) { sizeDp.dp.toSp() },
                    color = androidx.compose.material3.LocalContentColor.current,
                    textAlign = TextAlign.Center,
                    // Replicate the material3.Icon semantics: set contentDescription when non-null,
                    // leave the node undescribed (decorative) when null.
                    modifier = modifier
                        .size(sizeDp.dp)
                        .semantics {
                            if (contentDescription != null) this.contentDescription = contentDescription
                        },
                )
        }
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
