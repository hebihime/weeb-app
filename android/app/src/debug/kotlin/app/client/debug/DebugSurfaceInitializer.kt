package app.client.debug

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.net.Uri
import app.client.DebugSurface

/**
 * SLICE_S7_CONTRACT.md §9d/§1d — debug-only auto-initializer for the diagnostics / state-gallery
 * surface, using the ContentProvider-startup trick (the same mechanism androidx.startup and Firebase
 * use, with zero extra dependency): Android instantiates every manifest-declared ContentProvider and
 * calls onCreate() before Application.onCreate() finishes and before any Activity, so this wires the
 * surface into the main-owned [DebugSurface] registry at process start.
 *
 * The decoupling this enables: src/main never references the `app.client.debug` package. This class,
 * DiagnosticsAndGalleryScreen, DebugStateCopy, and the <provider> that starts it live ONLY in
 * src/debug (registered in src/debug/AndroidManifest.xml). A release build has no src/debug, so
 * [DebugSurface.entry] stays null and none of this code — nor the backend URL behind the diagnostics
 * screen — is compiled into release (fail-closed by absence).
 */
class DebugSurfaceInitializer : ContentProvider() {
    override fun onCreate(): Boolean {
        DebugSurface.entry = { DiagnosticsAndGalleryScreen() }
        return true
    }

    override fun query(
        uri: Uri,
        projection: Array<out String>?,
        selection: String?,
        selectionArgs: Array<out String>?,
        sortOrder: String?,
    ): Cursor? = null

    override fun getType(uri: Uri): String? = null

    override fun insert(uri: Uri, values: ContentValues?): Uri? = null

    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int = 0

    override fun update(
        uri: Uri,
        values: ContentValues?,
        selection: String?,
        selectionArgs: Array<out String>?,
    ): Int = 0
}
