package app.client

import android.content.res.Configuration
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import java.util.Locale
import app.client.appshell.AppShell
import app.client.appshell.AppShellStrings
import app.client.appshell.TabCopy
import app.client.debug.debugEntryPoint
import app.client.designkit.Palette
import app.client.designkit.tokenColor
import app.client.designkit.weebTypography
import app.client.features.signup.SignupFlow
import app.client.features.signup.SignupStrings
import app.client.features.signup.UnavailableSignupGateway

/**
 * SLICE_S7_CONTRACT.md §9b/§9c — the composition root. This is the ONE place that resolves Android
 * string resources: :designkit / :appshell / :features:signup take plain [String] parameters (§9a/§1b
 * module isolation — no library module reaches into another module's resources), so every user-facing
 * string in the whole client tree is threaded down from here.
 */
class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: android.os.Bundle?) {
        super.onCreate(savedInstanceState)

        // Debug-only locale test seam. §9e / DR-7.7: NO in-app language picker ships — locale follows
        // the device. This is NOT a picker; it is a Maestro launch-argument hook, gated to debug so a
        // release build has no locale-override code path at all. Maestro's ES-locale smoke launches
        // with `-e appLocale=es`, which arrives as an "appLocale" intent extra (launch arguments do
        // NOT change the emulator's system locale), and honoring it lets the flow assert the ES
        // handle-step title ("Elige tu usuario") without any device-level locale change.
        val overrideLocale =
            if (BuildConfig.DEBUG) intent?.extras?.getString("appLocale")?.takeIf { it.isNotBlank() } else null

        setContent {
            if (overrideLocale == null) {
                WeebAppRoot()
            } else {
                val base = LocalContext.current
                val localizedContext = remember(overrideLocale) {
                    val config = Configuration(base.resources.configuration)
                    config.setLocale(Locale.forLanguageTag(overrideLocale))
                    base.createConfigurationContext(config)
                }
                // stringResource reads LocalContext.current.resources (keyed on LocalConfiguration for
                // recomposition), so providing both makes every stringResource() resolve in the
                // overridden locale for the whole tree.
                CompositionLocalProvider(
                    LocalContext provides localizedContext,
                    LocalConfiguration provides localizedContext.resources.configuration,
                ) {
                    WeebAppRoot()
                }
            }
        }
    }
}

@OptIn(ExperimentalComposeUiApi::class)
@Composable
private fun WeebAppRoot() {
    // Force light mode at S7 (DR-6.3) — the Choco dark palette is carried in Tokens.kt but read by
    // nothing here; lightColorScheme is the only scheme this root ever constructs.
    val colorScheme = lightColorScheme(
        primary = tokenColor("#" + BuildConfig.BRAND_PRIMARY_HEX),
        secondary = tokenColor(Palette.Candy.sky),
        tertiary = tokenColor("#" + BuildConfig.BRAND_CELEBRATION_HEX),
        background = tokenColor(Palette.Light.ground),
        surface = tokenColor(Palette.Light.surface),
        onBackground = tokenColor(Palette.Light.text),
        onSurface = tokenColor(Palette.Light.text),
        error = tokenColor(Palette.Semantic.danger),
    )

    MaterialTheme(colorScheme = colorScheme, typography = weebTypography) {
        // testTagsAsResourceId is what makes every child's `Modifier.testTag("...")` visible to Maestro
        // as an Android resource-id (the `id:` selector maestro/flows/brand-smoke asserts against).
        // Without this one flag at the tree root, Compose testTags stay internal to the Compose
        // semantics tree and Maestro's id: matcher finds nothing — set once here, inherited by the
        // whole subtree (AppShell, SignupFlow, the debug surface). The contentDescription each element
        // ALSO sets is the TalkBack/a11y label; the two are complementary (maestro/README.md: the id
        // string == the a11y label).
        Surface(modifier = Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
            var showSignup by remember { mutableStateOf(false) }
            var showDebugSurface by remember { mutableStateOf(false) }
            val debugSurface: (@Composable () -> Unit)? = debugEntryPoint

            if (showDebugSurface && debugSurface != null) {
                debugSurface.invoke()
            } else if (showSignup) {
                SignupFlow(strings = rememberSignupStrings(), gateway = remember { UnavailableSignupGateway() })
            } else {
                val onOpenDebugSurface: (() -> Unit)? = if (debugSurface != null) {
                    { showDebugSurface = true }
                } else {
                    null
                }
                AppShell(
                    strings = rememberAppShellStrings(),
                    onStartSignup = { showSignup = true },
                    onOpenDebugSurface = onOpenDebugSurface,
                )
            }
        }
    }
}

@Composable
private fun rememberAppShellStrings(): AppShellStrings = AppShellStrings(
    wordmark = BuildConfig.BRAND_WORDMARK,
    connect = TabCopy(
        label = stringResource(R.string.tab_connect_label),
        emptyTitle = stringResource(R.string.state_connect_empty_title),
        emptyBody = stringResource(R.string.state_connect_empty_body),
        emptyCtaLabel = stringResource(R.string.state_connect_empty_cta),
    ),
    explore = TabCopy(
        label = stringResource(R.string.tab_explore_label),
        emptyTitle = stringResource(R.string.state_explore_empty_title),
        emptyBody = stringResource(R.string.state_explore_empty_body),
        emptyCtaLabel = stringResource(R.string.state_explore_empty_cta),
    ),
    crews = TabCopy(
        label = stringResource(R.string.tab_crews_label),
        emptyTitle = stringResource(R.string.state_crews_empty_title),
        emptyBody = stringResource(R.string.state_crews_empty_body),
        emptyCtaLabel = stringResource(R.string.state_crews_empty_cta),
    ),
    inbox = TabCopy(
        label = stringResource(R.string.tab_inbox_label),
        emptyTitle = stringResource(R.string.state_inbox_empty_title),
        emptyBody = stringResource(R.string.state_inbox_empty_body),
        emptyCtaLabel = stringResource(R.string.state_inbox_empty_cta),
    ),
    profile = TabCopy(
        label = stringResource(R.string.tab_profile_label),
        emptyTitle = stringResource(R.string.state_profile_empty_title),
        emptyBody = stringResource(R.string.state_profile_empty_body),
        emptyCtaLabel = "",
    ),
    profileSignupStartLabel = stringResource(R.string.profile_signup_start_label),
    crewsPremiumCtaLabel = stringResource(R.string.crews_create_premium_cta_label),
    inboxRequestsSectionTitle = stringResource(R.string.inbox_requests_section_title),
    inboxRequestsEmptyBody = stringResource(R.string.inbox_requests_empty_body),
    modeChipLabel = stringResource(R.string.mode_chip_label),
    debugEntryPointLabel = stringResource(R.string.debug_entry_point_label),
)

@Composable
private fun rememberSignupStrings(): SignupStrings = SignupStrings(
    startTitle = stringResource(R.string.signup_start_title),
    startCta = stringResource(R.string.signup_start_cta),
    handleTitle = stringResource(R.string.signup_handle_title),
    handleHint = stringResource(R.string.signup_handle_hint),
    handleInvalid = stringResource(R.string.signup_handle_invalid),
    handleNextCta = stringResource(R.string.signup_handle_next_cta),
    emailTitle = stringResource(R.string.signup_email_title),
    emailHint = stringResource(R.string.signup_email_hint),
    emailNextCta = stringResource(R.string.signup_email_next_cta),
    birthdateTitle = stringResource(R.string.signup_birthdate_title),
    birthdateHint = stringResource(R.string.signup_birthdate_hint),
    birthdateNextCta = stringResource(R.string.signup_birthdate_next_cta),
    ageRefusalNeutralTitle = stringResource(R.string.signup_age_refusal_neutral_title),
    ageRefusalNeutralBody = stringResource(R.string.signup_age_refusal_neutral_body),
    ageRefusalCoppaTitle = stringResource(R.string.signup_age_refusal_coppa_title),
    ageRefusalCoppaBody = stringResource(R.string.signup_age_refusal_coppa_body),
    avatarTitle = stringResource(R.string.signup_avatar_title),
    avatarSkipCta = stringResource(R.string.signup_avatar_skip_cta),
    fandomTitle = stringResource(R.string.signup_fandom_title),
    fandomOptionLabels = listOf(
        stringResource(R.string.signup_fandom_option_0),
        stringResource(R.string.signup_fandom_option_1),
        stringResource(R.string.signup_fandom_option_2),
    ),
    submitCta = stringResource(R.string.signup_submit_cta),
    couldNotSendTitle = stringResource(R.string.signup_could_not_send_title),
    couldNotSendBody = stringResource(R.string.signup_could_not_send_body),
)
