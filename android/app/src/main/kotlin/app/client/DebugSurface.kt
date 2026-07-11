package app.client

import androidx.compose.runtime.Composable

/**
 * SLICE_S7_CONTRACT.md §9d — the decoupling seam between src/main and the debug-only diagnostics /
 * state-gallery surface. src/main OWNS this registry and never references the `app.client.debug`
 * package (which exists only in src/debug); a debug build wires [entry] at process start via a
 * ContentProvider declared in src/debug/AndroidManifest.xml (the same auto-init trick androidx.startup
 * uses). Release builds contain no src/debug at all, so [entry] stays null and no debug/diagnostics
 * code — nor any backend-URL reference behind it — is ever compiled into release (fail-closed by
 * absence). Because main only ever touches its own [DebugSurface], it compiles in EVERY variant.
 */
object DebugSurface {
    var entry: (@Composable () -> Unit)? = null
}
