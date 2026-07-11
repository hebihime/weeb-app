package app.client.designkit

import androidx.compose.ui.graphics.Color

/**
 * `tokenColor(hex)` for our own token strings only — never call this with a literal outside [Palette].
 * Mirrors ios/DesignKit/Sources/DesignKit/Tokens.swift's `Color(tokenHex:)` convenience 1:1.
 */
fun tokenColor(hex: String): Color {
    val clean = hex.removePrefix("#")
    val value = clean.toLong(16)
    return if (clean.length == 6) {
        Color(
            red = ((value shr 16) and 0xFF).toInt(),
            green = ((value shr 8) and 0xFF).toInt(),
            blue = (value and 0xFF).toInt(),
        )
    } else {
        Color(value.toInt())
    }
}
