package app.client.features.signup

import java.time.LocalDate
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class BirthdateValidatorTest {
    private val today = LocalDate.of(2026, 7, 10)

    @Test
    fun `17-year-old is refused, not COPPA`() {
        val birthdate = today.minusYears(17)
        assertEquals(AgeGateResult.RefusedUnder18, BirthdateValidator.evaluate(birthdate, today))
    }

    @Test
    fun `18-year-old passes`() {
        val birthdate = today.minusYears(18)
        assertEquals(AgeGateResult.Allowed, BirthdateValidator.evaluate(birthdate, today))
    }

    @Test
    fun `12-year-old hits the hard COPPA refusal`() {
        val birthdate = today.minusYears(12)
        assertEquals(AgeGateResult.RefusedCoppaUnder13, BirthdateValidator.evaluate(birthdate, today))
    }

    @Test
    fun `exactly 13 is the neutral refusal, not COPPA`() {
        val birthdate = today.minusYears(13)
        assertEquals(AgeGateResult.RefusedUnder18, BirthdateValidator.evaluate(birthdate, today))
    }

    @Test
    fun `one day before the 18th birthday still refuses`() {
        val birthdate = today.minusYears(18).plusDays(1)
        assertEquals(AgeGateResult.RefusedUnder18, BirthdateValidator.evaluate(birthdate, today))
    }

    @Test
    fun `a very old attested age passes`() {
        val birthdate = today.minusYears(90)
        assertEquals(AgeGateResult.Allowed, BirthdateValidator.evaluate(birthdate, today))
    }

    @Test
    fun `a future birthdate is Invalid, never a COPPA verdict (SEC-S7-F1)`() {
        val future = today.plusYears(5)
        assertEquals(AgeGateResult.Invalid, BirthdateValidator.evaluate(future, today))
        // Boundary: today itself is age 0 -> COPPA-refused (a real past-or-present date), NOT Invalid.
        assertEquals(AgeGateResult.RefusedCoppaUnder13, BirthdateValidator.evaluate(today, today))
    }

    @Test
    fun `parse accepts ISO dates and rejects garbage`() {
        assertEquals(LocalDate.of(2000, 1, 1), BirthdateValidator.parse("2000-01-01"))
        assertNull(BirthdateValidator.parse("not-a-date"))
        assertNull(BirthdateValidator.parse(""))
    }
}
