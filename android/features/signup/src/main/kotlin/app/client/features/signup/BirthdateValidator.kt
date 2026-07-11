package app.client.features.signup

import java.time.LocalDate
import java.time.Period
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeParseException

/**
 * SLICE_S7_CONTRACT.md Correction 1 — the native apps are actor A2 (18+); the S9 web funnel is the
 * ONLY under-18-visible surface. This gate refuses any attested age UNDER 18 (neutral-plain refusal),
 * with attested UNDER-13 rendering the distinct hard COPPA-refusal copy as a sub-case. Client-side
 * attestation only — server-side 18+ enforcement + estimation-first verification are S3/S18 — but an
 * 18+ app whose signup shell lets a 15-year-old through the form is a fabricated-honesty defect (L6)
 * caught here for the cost of one validation rule.
 */
enum class AgeGateResult { Allowed, RefusedUnder18, RefusedCoppaUnder13 }

object BirthdateValidator {
    private val ISO = DateTimeFormatter.ISO_LOCAL_DATE

    fun parse(input: String): LocalDate? = try {
        LocalDate.parse(input.trim(), ISO)
    } catch (e: DateTimeParseException) {
        null
    }

    fun evaluate(birthdate: LocalDate, today: LocalDate = LocalDate.now()): AgeGateResult {
        val age = Period.between(birthdate, today).years
        return when {
            age < 13 -> AgeGateResult.RefusedCoppaUnder13
            age < 18 -> AgeGateResult.RefusedUnder18
            else -> AgeGateResult.Allowed
        }
    }
}
