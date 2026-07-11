package app.client.appshell

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.IconButton
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import app.client.designkit.Glyph
import app.client.designkit.IconRegister
import app.client.designkit.Iconography
import app.client.designkit.Spacing
import app.client.designkit.components.CrewsPremiumCta
import app.client.designkit.components.ListRow
import app.client.designkit.state.Register
import app.client.designkit.state.StateView

/** DR-1.1 LOCKED. The Quests tab does NOT exist as a value here — pre-G4 absence, not a hidden case. */
enum class AppTab(val testTag: String, val glyph: Glyph) {
    Connect("tab.connect", Glyph.TabConnect),
    Explore("tab.explore", Glyph.TabExplore),
    Crews("tab.crews", Glyph.TabCrews),
    Inbox("tab.inbox", Glyph.TabInbox),
    Profile("tab.profile", Glyph.TabProfile),
}

data class TabCopy(
    val label: String,
    val emptyTitle: String,
    val emptyBody: String,
    val emptyCtaLabel: String,
)

data class AppShellStrings(
    val wordmark: String,
    val connect: TabCopy,
    val explore: TabCopy,
    val crews: TabCopy,
    val inbox: TabCopy,
    val profile: TabCopy,
    val profileSignupStartLabel: String,
    val crewsPremiumCtaLabel: String,
    val inboxRequestsSectionTitle: String,
    val inboxRequestsEmptyBody: String,
    val modeChipLabel: String,
    val debugEntryPointLabel: String,
)

/**
 * SLICE_S7_CONTRACT.md §9b — the ONE shared shell layout. Trunk test on every screen: brand mark +
 * active tab always visible; the mode chip renders ONLY when [mode] != [ModeContext.Online] — which is
 * never at S7, since [currentMode] can only ever return [ModeContext.Online]. This function still
 * DEFINES the chip (so the branch is a real, compiled, structurally-unreachable path, not a missing
 * feature) — it just never executes it while the sole producer stays [ModeContext.Online].
 */
@Composable
fun AppShell(
    strings: AppShellStrings,
    onStartSignup: () -> Unit,
    mode: ModeContext = currentMode(),
    modifier: Modifier = Modifier,
    onOpenDebugSurface: (() -> Unit)? = null,
) {
    // Profile is the default selected tab: the boot is anonymous by construction (§3, no account yet),
    // so "signup.start" (this tab's live CTA) must be reachable on first launch with zero navigation —
    // maestro/flows/brand-smoke's ES-locale leg taps it immediately after asserting the wordmark, with
    // no prior tab.profile tap (a fresh `launchApp: clearState: true` run).
    var selectedTab by remember { mutableStateOf(AppTab.Profile) }

    Scaffold(
        modifier = modifier.fillMaxSize(),
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        text = strings.wordmark,
                        modifier = Modifier
                            .testTag("brand.wordmark")
                            .semantics { contentDescription = strings.wordmark },
                    )
                },
                actions = {
                    if (onOpenDebugSurface != null) {
                        IconButton(onClick = onOpenDebugSurface) {
                            Iconography.icon(
                                glyph = Glyph.ChevronForward,
                                register = IconRegister.Neutral,
                                contentDescription = strings.debugEntryPointLabel,
                            )
                        }
                    }
                },
            )
        },
        bottomBar = {
            NavigationBar {
                for (tab in AppTab.entries) {
                    val label = tab.label(strings)
                    NavigationBarItem(
                        selected = selectedTab == tab,
                        onClick = { selectedTab = tab },
                        icon = {
                            Iconography.icon(glyph = tab.glyph, register = IconRegister.Playful, contentDescription = null)
                        },
                        label = { Text(text = label) },
                        modifier = Modifier
                            .testTag(tab.testTag)
                            .semantics { contentDescription = label },
                    )
                }
            }
        },
    ) { innerPadding ->
        Column(modifier = Modifier.padding(innerPadding)) {
            if (mode != ModeContext.Online) {
                ModeChip(label = strings.modeChipLabel)
            }
            when (selectedTab) {
                AppTab.Connect -> ConnectTab(strings.connect, onCta = { selectedTab = AppTab.Explore })
                AppTab.Explore -> ExploreTab(strings.explore, onCta = { selectedTab = AppTab.Crews })
                AppTab.Crews -> CrewsTab(strings.crews, premiumCtaLabel = strings.crewsPremiumCtaLabel, onCta = { selectedTab = AppTab.Inbox })
                AppTab.Inbox -> InboxTab(
                    strings.inbox,
                    requestsSectionTitle = strings.inboxRequestsSectionTitle,
                    requestsEmptyBody = strings.inboxRequestsEmptyBody,
                    onCta = { selectedTab = AppTab.Profile },
                )
                AppTab.Profile -> ProfileTab(strings.profile, signupStartLabel = strings.profileSignupStartLabel, onStartSignup = onStartSignup)
            }
        }
    }
}

private fun AppTab.label(strings: AppShellStrings): String = when (this) {
    AppTab.Connect -> strings.connect.label
    AppTab.Explore -> strings.explore.label
    AppTab.Crews -> strings.crews.label
    AppTab.Inbox -> strings.inbox.label
    AppTab.Profile -> strings.profile.label
}

/**
 * Mode chip — DEFINED, never rendered at S7 (see [AppShell]'s doc). Rendering this here at all with
 * live data would be fabrication (L6); the only caller is the dead `mode != Online` branch above.
 */
@Composable
private fun ModeChip(label: String) {
    Text(
        text = label,
        modifier = Modifier
            .testTag("mode.chip")
            .semantics { contentDescription = label },
    )
}

@Composable
private fun ConnectTab(copy: TabCopy, onCta: () -> Unit) {
    StateView(
        title = copy.emptyTitle,
        body = copy.emptyBody,
        glyph = Glyph.EmptyDeck,
        register = Register.Playful,
        testTag = "state.connect.empty",
        ctaLabel = copy.emptyCtaLabel,
        onCta = onCta,
    )
}

@Composable
private fun ExploreTab(copy: TabCopy, onCta: () -> Unit) {
    StateView(
        title = copy.emptyTitle,
        body = copy.emptyBody,
        glyph = Glyph.EmptyExplore,
        register = Register.Playful,
        testTag = "state.explore.empty",
        ctaLabel = copy.emptyCtaLabel,
        onCta = onCta,
    )
}

@Composable
private fun CrewsTab(copy: TabCopy, premiumCtaLabel: String, onCta: () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(Spacing.scale[2].dp)) {
        StateView(
            title = copy.emptyTitle,
            body = copy.emptyBody,
            glyph = Glyph.EmptyCrews,
            register = Register.Playful,
            testTag = "state.crews.empty",
            ctaLabel = copy.emptyCtaLabel,
            onCta = onCta,
        )
        // The SOLE ratified law-3 exception (design/tokens.v1.json allowlisted_exceptions). A live,
        // tappable CTA — routed by the caller (:app composes the real upsell surface later); never a
        // grayed rendering of the primary CTA.
        CrewsPremiumCta(label = premiumCtaLabel, onClick = onCta)
    }
}

@Composable
private fun InboxTab(
    copy: TabCopy,
    requestsSectionTitle: String,
    requestsEmptyBody: String,
    onCta: () -> Unit,
) {
    Column {
        StateView(
            title = copy.emptyTitle,
            body = copy.emptyBody,
            glyph = Glyph.EmptyInbox,
            register = Register.Playful,
            testTag = "state.inbox.empty",
            ctaLabel = copy.emptyCtaLabel,
            onCta = onCta,
        )
        // DR-2.2 Requests section slot, empty (§8) — rendered inline under the same live empty state,
        // never fake counts/people (L6).
        ListRow(title = requestsSectionTitle, caption = requestsEmptyBody)
    }
}

/**
 * Own-profile preview-first empty (§9b). The boot is anonymous by construction (§3) — there is no
 * account yet, so this tab's live action IS the entry point into the 5.14a signup shell. The CTA
 * carries the "signup.start" Maestro/accessibility ID directly (maestro/README.md) rather than the
 * generic per-tab CTA pattern the other four tabs use.
 */
@Composable
private fun ProfileTab(copy: TabCopy, signupStartLabel: String, onStartSignup: () -> Unit) {
    StateView(
        title = copy.emptyTitle,
        body = copy.emptyBody,
        glyph = Glyph.EmptyProfile,
        register = Register.Playful,
        testTag = "state.profile.empty",
        ctaLabel = signupStartLabel,
        onCta = onStartSignup,
        ctaTestTag = "signup.start",
    )
}
