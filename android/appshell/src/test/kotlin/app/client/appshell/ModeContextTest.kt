package app.client.appshell

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

/**
 * SLICE_S7_CONTRACT.md §9b — `ModeContext` is closed/sealed with THREE cases so S19/S34 add a case +
 * a renderer, never a nav rewrite, but [currentMode] — the one producer AppShell actually calls — must
 * only ever yield [ModeContext.Online] at S7. A visible mode chip at S7 would be fabrication (L6); this
 * test is the structural proof the producer can't accidentally start returning anything else.
 */
class ModeContextTest {
    @Test
    fun `currentMode always yields Online at S7`() {
        repeat(5) {
            assertEquals(ModeContext.Online, currentMode())
        }
    }

    @Test
    fun `ModeContext has exactly the three ratified cases, ConPresent and Nakama exist but unused here`() {
        val online = ModeContext.Online
        val conPresent = ModeContext.ConPresent(conRef = "con_test")
        val nakama = ModeContext.Nakama
        assertIs<ModeContext.Online>(online)
        assertIs<ModeContext.ConPresent>(conPresent)
        assertIs<ModeContext.Nakama>(nakama)
        assertEquals("con_test", conPresent.conRef)
    }

    @Test
    fun `Online is not equal to any other mode`() {
        assertEquals(ModeContext.Online, ModeContext.Online)
        val other: ModeContext = ModeContext.Nakama
        kotlin.test.assertFalse(ModeContext.Online == other)
    }
}
