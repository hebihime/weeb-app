package app.client.debug

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import app.client.BuildConfig
import app.client.apikit.ClientResult
import app.client.apikit.Transport
import app.client.designkit.Spacing
import app.client.designkit.components.ListRow
import app.client.designkit.state.DesignState
import app.client.designkit.state.StateView
import kotlinx.coroutines.launch

/**
 * SLICE_S7_CONTRACT.md §1d / §9d — the debug-build-only diagnostics screen (renders `GET
 * /v1/client-config`, read BACK, never emit-only) AND the debug-build-only state gallery (all 23
 * states). This whole file lives under `src/debug/` — Android's own source-set mechanism is what makes
 * "never compiled into release" a structural guarantee, not a runtime flag (§9d L15 posture).
 */
@Composable
fun DiagnosticsAndGalleryScreen() {
    var selectedState by remember { mutableStateOf<DesignState?>(null) }
    val shown = selectedState

    if (shown != null) {
        val copy = DebugStateCopy.describe(shown)
        Column {
            Button(onClick = { selectedState = null }) { Text(text = "Back to gallery") }
            StateView(title = copy.title, body = copy.body, glyph = shown.glyph, register = shown.register)
        }
        return
    }

    var diagnosticsResult by remember { mutableStateOf("Not checked yet") }
    val scope = rememberCoroutineScope()

    LazyColumn(modifier = Modifier.fillMaxSize().padding(Spacing.scale[3].dp)) {
        item {
            Column {
                Text(text = "Diagnostics — GET /v1/client-config")
                Text(text = diagnosticsResult)
                Button(onClick = {
                    scope.launch {
                        val transport = Transport(baseUrl = BuildConfig.API_BASE_URL)
                        diagnosticsResult = when (val result = transport.getClientConfig()) {
                            is ClientResult.Ok ->
                                "apiVersion=${result.value.apiVersion} locales=${result.value.locales} defaultLocale=${result.value.defaultLocale}"
                            is ClientResult.Denied -> "denied (limit reached — unexpected for this endpoint)"
                            is ClientResult.Problematic -> "problem: ${result.messageKey}"
                            ClientResult.Offline -> "offline — is the compose backend running at ${BuildConfig.API_BASE_URL}?"
                        }
                    }
                }) { Text(text = "Check client-config") }
            }
        }
        item {
            Text(text = "State gallery — ${DesignState.entries.size} states (§9d)")
        }
        items(DesignState.entries.toList()) { state ->
            val copy = DebugStateCopy.describe(state)
            Column(modifier = Modifier.padding(vertical = Spacing.scale[0].dp)) {
                ListRow(
                    title = copy.title,
                    caption = if (state.reachableLive) "LIVE at S7" else "gallery-only",
                )
                Button(onClick = { selectedState = state }) { Text(text = "View") }
            }
        }
    }
}
