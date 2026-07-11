package app.client

import android.app.Application

/**
 * SLICE_S7_CONTRACT.md §9f — "deliberately ZERO client analytics at S7 — no account, no consent
 * surface, nothing lawful to emit." This Application class does nothing beyond the platform default on
 * purpose: no analytics SDK init, no crash-reporter init (crash telemetry is platform-native opt-in
 * only, never a bundled SDK — egress-lint's tracker denylist is what makes an accidental future add red).
 */
class WeebApplication : Application()
