package app.client.features.signup

/** Real client-side validation (§9c): handle charset — lowercase letters, digits, underscore, 3-20 chars. */
object HandleValidator {
    private val PATTERN = Regex("^[a-z0-9_]{3,20}$")

    fun isValid(handle: String): Boolean = PATTERN.matches(handle.trim().lowercase())
}
