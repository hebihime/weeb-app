package app.client.snapshot

import androidx.activity.ComponentActivity
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onRoot
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.client.BuildConfig
import app.client.R
import app.client.designkit.Glyph
import app.client.designkit.Palette
import app.client.designkit.state.Register
import app.client.designkit.state.StateView
import app.client.designkit.tokenColor
import app.client.designkit.weebTypography
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * SLICE_S7_CONTRACT.md §9d / §12 — "Snapshot suite covering the state kit per flavor per locale."
 * Per-FLAVOR coverage comes for free from Gradle: this same test class runs once under
 * `testWeebDebugUnitTest` (BuildConfig.BRAND_PRIMARY_HEX = Bubblegum) and once under
 * `testFrikiDebugUnitTest` (BRAND_PRIMARY_HEX = Tangerine) — the CTA button color is the one place S7
 * actually paints brand.primary, so the two flavor runs produce genuinely different images. Per-LOCALE
 * coverage uses Robolectric's `@Config(qualifiers=...)`, the standard mechanism for resolving a
 * specific resource-qualifier set without an in-app language picker (§9e: none exists at S7).
 *
 * This covers the five LIVE tab-empty states (real, translated copy in :app's res). The other 18
 * catalog entries are debug-gallery-only with English-only QA copy (DebugStateCopy) — snapshotting
 * placeholder English text four times per locale would assert nothing real, so they are exercised by
 * DesignKit's `NoDisabledTokenTest` (structural) and the debug gallery itself (manual UX-coherence
 * crawl, §12 evidence 5) instead of a per-locale image diff.
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class StateKitSnapshotTest {
    // ComponentActivity (not RoborazziActivity): the `androidx.compose.ui:ui-test-manifest`
    // debugImplementation adds exactly `androidx.activity.ComponentActivity` to the test manifest, so
    // this is the documented, lowest-dependency pairing — it does not rely on Roborazzi shipping its
    // own activity into the merged manifest.
    @get:Rule
    val composeTestRule = createAndroidComposeRule<ComponentActivity>()

    private fun brandColorScheme() = lightColorScheme(
        primary = tokenColor("#" + BuildConfig.BRAND_PRIMARY_HEX),
        secondary = tokenColor(Palette.Candy.sky),
        tertiary = tokenColor("#" + BuildConfig.BRAND_CELEBRATION_HEX),
        background = tokenColor(Palette.Light.ground),
        surface = tokenColor(Palette.Light.surface),
    )

    private fun captureConnectEmpty(name: String) {
        composeTestRule.setContent {
            MaterialTheme(colorScheme = brandColorScheme(), typography = weebTypography) {
                Surface {
                    StateView(
                        title = stringResource(R.string.state_connect_empty_title),
                        body = stringResource(R.string.state_connect_empty_body),
                        glyph = Glyph.EmptyDeck,
                        register = Register.Playful,
                        testTag = "state.connect.empty",
                        ctaLabel = stringResource(R.string.state_connect_empty_cta),
                        onCta = {},
                    )
                }
            }
        }
        composeTestRule.onRoot().captureRoboImage(
            "src/test/snapshots/images/StateKit_${BuildConfig.BRAND_KEY}_${name}_connectEmpty.png",
        )
    }

    @Test
    @Config(qualifiers = "en")
    fun `connect empty state - en`() = captureConnectEmpty("en")

    @Test
    @Config(qualifiers = "es")
    fun `connect empty state - es`() = captureConnectEmpty("es")

    @Test
    @Config(qualifiers = "pt")
    fun `connect empty state - pt`() = captureConnectEmpty("pt")

    @Test
    @Config(qualifiers = "b+zh+Hans")
    fun `connect empty state - zh-Hans`() = captureConnectEmpty("zhHans")
}
