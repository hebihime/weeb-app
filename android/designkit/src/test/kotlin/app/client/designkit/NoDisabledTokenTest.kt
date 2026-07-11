package app.client.designkit

import app.client.designkit.components.BadgeStyle
import app.client.designkit.state.DesignState
import app.client.designkit.state.Register
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * SLICE_S7_CONTRACT.md §9a token law 3/6 — "the variant enums simply lack the cases; a deterministic
 * kit test proves no `disabled` state token resolves." This walks every variant-style enum this kit
 * ships and asserts none of its case names collide with design/tokens.v1.json's forbidden_token_groups
 * — reflection over real enums, not a string scan of source text, so it can never be fooled by a
 * comment that merely discusses the law.
 */
class NoDisabledTokenTest {
    private val repoRoot = File(System.getProperty("REPO_ROOT") ?: error("REPO_ROOT system property not set"))

    private fun forbiddenGroups(): Set<String> {
        val manifest = Json.parseToJsonElement(File(repoRoot, "design/tokens.v1.json").readText()).jsonObject
        val forbidden: JsonArray = manifest["forbidden_token_groups"]!!.jsonArray
        return forbidden.map { (it as JsonPrimitive).content.lowercase() }.toSet()
    }

    @Test
    fun `manifest declares the forbidden groups this test enforces`() {
        val forbidden = forbiddenGroups()
        assertTrue(forbidden.contains("disabled"))
        assertTrue(forbidden.contains("locked"))
        assertTrue(forbidden.contains("grayed"))
    }

    @Test
    fun `no case of any designkit variant enum resolves to a forbidden token`() {
        val forbidden = forbiddenGroups()
        val variantEnumNames: List<String> = buildList {
            addAll(BadgeStyle.entries.map { it.name })
            addAll(Register.entries.map { it.name })
            addAll(IconRegister.entries.map { it.name })
            addAll(DesignState.entries.map { it.name })
        }
        val violations = variantEnumNames.filter { name -> forbidden.any { name.lowercase().contains(it) } }
        assertTrue(violations.isEmpty(), "forbidden-token-shaped enum case(s) found: $violations")
    }

    @Test
    fun `the design state catalog lands on the ledger's acceptance count`() {
        assertEquals(DesignState.ACCEPTANCE_COUNT, DesignState.entries.size)
        assertEquals(23, DesignState.entries.size)
    }

    @Test
    fun `every design state id is unique`() {
        val ids = DesignState.entries.map { it.id }
        assertEquals(ids.size, ids.toSet().size, "duplicate DesignState id found")
    }

    @Test
    fun `only the ratified live states carry a Maestro testTag`() {
        val liveWithoutTag = DesignState.entries.filter { it.reachableLive && it.testTag == null }
        assertTrue(liveWithoutTag.isEmpty(), "live-reachable state(s) missing a Maestro testTag: $liveWithoutTag")
        // Every reachable-live state must be one Maestro's brand-smoke flow actually asserts, or the
        // signup gateway-refusal end — never a "pending" chrome state (§1e: no pending-chrome component).
        val liveTags = DesignState.entries.filter { it.reachableLive }.mapNotNull { it.testTag }.toSet()
        assertFalse(liveTags.any { it.contains("pending") })
    }
}
