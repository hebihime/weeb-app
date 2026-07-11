package app.client.appshell

/**
 * SLICE_S7_CONTRACT.md §9b (DR-1.1 LOCKED) — closed/sealed now so S19/S34 add a case + a renderer,
 * never a nav rewrite. `Online` is the ONLY constructible case at S7 — nothing in this module ever
 * builds a [ConPresent] or [Nakama] instance; those constructors exist for the future slice, not for
 * S7 to exercise. Mode is context, not place: no manual mode switch UI exists or ever will.
 */
sealed class ModeContext {
    data object Online : ModeContext()
    data class ConPresent(val conRef: String) : ModeContext()
    data object Nakama : ModeContext()
}

/**
 * The one producer of [ModeContext] at S7. A function (not a constant) so a later slice swaps the
 * implementation (real con/presence detection) without changing any call site's type.
 */
fun currentMode(): ModeContext = ModeContext.Online
