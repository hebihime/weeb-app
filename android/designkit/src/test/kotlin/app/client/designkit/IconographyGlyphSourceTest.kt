package app.client.designkit

import androidx.compose.ui.text.font.FontFamily
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import kotlin.test.assertEquals
import kotlin.test.assertSame
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §9a Correction 2 — the Iconography seam must resolve every semantic [Glyph]
 * whether or not the gitignored Font Awesome Pro `.otf` faces are present. These tests are
 * ENV-INDEPENDENT: they exercise the PURE parser ([FontAwesomeAssets.parseCodepoints]), the PURE
 * chooser ([chooseSource]), and the bundled [FallbackGlyphSource] — none of which touch the font
 * files. So the suite passes identically on a licensed box (fonts present) and on CI (fonts absent).
 *
 * Runs under Robolectric's [AndroidJUnit4] so `org.json` (an Android built-in, used by the parser
 * instead of adding a dependency) resolves to its real implementation rather than the android.jar stub.
 */
@RunWith(AndroidJUnit4::class)
class IconographyGlyphSourceTest {
    private val androidRoot =
        File(System.getProperty("ANDROID_ROOT") ?: error("ANDROID_ROOT system property not set"))
    private val realMapFile = File(androidRoot, "designkit/src/main/assets/fa-glyph-map.json")

    // A tiny hand-written fixture covering the three interesting cases: a 4-hex PUA codepoint
    // (checkmark = circle-check), an uppercase-hex 4-digit codepoint (tabConnect = users, F0C0), and a
    // 2-hex ASCII codepoint (signupHandle = at, 0x40). Keys are camelCase like the real asset.
    private val fixtureJson = """
        {
          "faces": { "playful": { "androidAsset": "fonts/solid.otf" } },
          "glyphs": {
            "checkmark": { "fa": "circle-check", "unicode": "f058" },
            "tabConnect": { "fa": "users", "unicode": "f0c0" },
            "signupHandle": { "fa": "at", "unicode": "40" }
          }
        }
    """.trimIndent()

    @Test
    fun `parser maps camelCase keys to Glyph enum and hex unicode to Int`() {
        val map = FontAwesomeAssets.parseCodepoints(fixtureJson)
        assertEquals(0xF058, map[Glyph.Checkmark])
        assertEquals(0xF0C0, map[Glyph.TabConnect])
        assertEquals(0x40, map[Glyph.SignupHandle])
        assertEquals(3, map.size)
    }

    @Test
    fun `real bundled asset parses to a codepoint for every Glyph entry`() {
        assertTrue(realMapFile.exists(), "canonical asset missing at ${realMapFile.absolutePath}")
        val map = FontAwesomeAssets.parseCodepoints(realMapFile.readText())
        assertEquals(27, Glyph.entries.size)
        assertEquals(27, map.size)
        val missing = Glyph.entries.filter { it !in map }
        assertTrue(missing.isEmpty(), "Glyph(s) with no FA codepoint in the canonical map: $missing")
        // Spot-check the same anchors the fixture uses, now against the real committed data.
        assertEquals(0xF058, map[Glyph.Checkmark])
        assertEquals(0xF0C0, map[Glyph.TabConnect])
        assertEquals(0x40, map[Glyph.SignupHandle])
    }

    @Test
    fun `chooseSource returns the FA source when present and the fallback when null`() {
        assertSame(FallbackGlyphSource, chooseSource(null))
        val fa = FontAwesomeGlyphSource(
            solid = FontFamily.Default,
            regular = FontFamily.Default,
            codepoints = FontAwesomeAssets.parseCodepoints(realMapFile.readText()),
        )
        assertSame(fa, chooseSource(fa))
    }

    @Test
    fun `fallback source resolves a Vector render for every Glyph entry`() {
        for (glyph in Glyph.entries) {
            for (register in IconRegister.entries) {
                val render = FallbackGlyphSource.resolve(glyph, register)
                assertTrue(
                    render is GlyphRender.Vector,
                    "fallback returned non-Vector for $glyph/$register: $render",
                )
            }
        }
    }

    @Test
    fun `FA source resolves Solid for playful and celebration, Regular for neutral`() {
        // Distinct sentinel families so we can assert which face each register selects.
        val solidFamily = FontFamily.Monospace
        val regularFamily = FontFamily.SansSerif
        val fa = FontAwesomeGlyphSource(
            solid = solidFamily,
            regular = regularFamily,
            codepoints = mapOf(Glyph.Checkmark to 0xF058),
        )
        val playful = fa.resolve(Glyph.Checkmark, IconRegister.Playful) as GlyphRender.FaGlyph
        val neutral = fa.resolve(Glyph.Checkmark, IconRegister.Neutral) as GlyphRender.FaGlyph
        val empty = fa.resolve(Glyph.Checkmark, IconRegister.EmptyOrCelebration) as GlyphRender.FaGlyph
        assertSame(solidFamily, playful.fontFamily)
        assertSame(regularFamily, neutral.fontFamily)
        assertSame(solidFamily, empty.fontFamily)
        assertEquals(0xF058, playful.codepoint)
    }
}
