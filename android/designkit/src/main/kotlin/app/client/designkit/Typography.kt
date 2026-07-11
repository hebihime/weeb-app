package app.client.designkit

import androidx.compose.material3.Typography
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/**
 * SLICE_S7_CONTRACT.md §9a — "Fonts: reference M PLUS Rounded 1c by family; do NOT commit large font
 * binaries (Julien asset-drop item); use a placeholder font family fallback so it compiles." Unlike
 * iOS's `Font.custom(name:)` (which gracefully substitutes the system font when a named face is
 * missing), Android's Compose `Font(resId)` requires an ACTUAL bundled font resource to even compile —
 * there is no runtime-graceful "look up this family name and fall back" path. [weebFontFamily] is
 * therefore [FontFamily.Default] (the system font) today, named as the ONE seam this whole client tree
 * reads for its font: once the OFL `.ttf` files land under `designkit/src/main/res/font/` (a Julien
 * asset-drop task exactly like Font Awesome Pro, Correction 2 — the files are free/OFL but this build
 * deliberately does not commit font binaries), swapping this one property to a real
 * `FontFamily(Font(R.font.m_plus_rounded_1c_regular), ...)` is the entire migration; no call site
 * changes because every Text style already reads through [Typography] below, never a literal FontFamily.
 */
val weebFontFamily: FontFamily = FontFamily.Default

private fun textStyle(role: TypeScale.Role, family: FontFamily = weebFontFamily): TextStyle = TextStyle(
    fontFamily = family,
    fontSize = role.size.sp,
    lineHeight = role.line.sp,
    fontWeight = FontWeight(role.weight),
)

/**
 * Maps design/tokens.v1.json's `type_scale` onto Compose Material3's [Typography] roles. `neutral`
 * register components read [TypeScale.neutralAllowedWeights] directly rather than through this object
 * (Black 900 never appears on safety surfaces — DESIGN.md Voice / token law 2 kin).
 */
val weebTypography: Typography = Typography(
    displayLarge = textStyle(TypeScale.displayXl),
    displayMedium = textStyle(TypeScale.display),
    titleLarge = textStyle(TypeScale.title),
    titleMedium = textStyle(TypeScale.heading),
    bodyLarge = textStyle(TypeScale.body),
    bodySmall = textStyle(TypeScale.caption),
    labelSmall = textStyle(TypeScale.microLabel),
)
