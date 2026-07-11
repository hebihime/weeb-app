package app.client.features.signup

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import app.client.designkit.Glyph
import app.client.designkit.Spacing
import app.client.designkit.state.ProblemSurface
import app.client.designkit.state.Register
import app.client.designkit.state.StateView
import kotlinx.coroutines.launch

/**
 * SLICE_S7_CONTRACT.md §9c — handle → verified-email step → birthdate attest → avatar-or-skip → one
 * fandom tag, fully navigable with real client-side validation. Submission crosses [SignupGateway];
 * S7 wires only [UnavailableSignupGateway], so the designed end of every successful walk is the honest
 * `error.could_not_send` state — never a fabricated success (L6).
 */
private sealed interface Step {
    data object Handle : Step
    data object Email : Step
    data object Birthdate : Step
    data object AgeRefusalNeutral : Step
    data object AgeRefusalCoppa : Step
    data object Avatar : Step
    data object Fandom : Step
    data object CouldNotSend : Step
}

@Composable
fun SignupFlow(
    strings: SignupStrings,
    gateway: SignupGateway,
    modifier: Modifier = Modifier,
) {
    // No separate "start" screen/tap exists inside this flow: the entry point ("signup.start") is the
    // caller's CTA (AppShell's Profile-tab empty state, §9b) — by the time this composable mounts, the
    // user has already crossed that threshold once, so the walk begins directly at Handle. Maestro's
    // signup-walk.yaml taps "signup.start" exactly once and expects "signup.handle" immediately after.
    var step by remember { mutableStateOf<Step>(Step.Handle) }
    var handle by remember { mutableStateOf("") }
    var email by remember { mutableStateOf("") }
    var birthdateInput by remember { mutableStateOf("") }
    var fandomTag by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(Spacing.scale[3].dp),
        verticalArrangement = Arrangement.spacedBy(Spacing.scale[2].dp),
    ) {
        when (step) {
            Step.Handle -> {
                Text(text = strings.startTitle)
                Text(text = strings.handleTitle)
                OutlinedTextField(
                    value = handle,
                    onValueChange = { handle = it },
                    placeholder = { Text(text = strings.handleHint) },
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("signup.handle")
                        .semantics { contentDescription = strings.handleTitle },
                )
                if (handle.isNotEmpty() && !HandleValidator.isValid(handle)) {
                    Text(text = strings.handleInvalid)
                }
                Button(
                    onClick = { if (HandleValidator.isValid(handle)) step = Step.Email },
                    modifier = Modifier.testTag("signup.handle.next").semantics { contentDescription = strings.handleNextCta },
                ) { Text(text = strings.handleNextCta) }
            }

            Step.Email -> {
                Text(text = strings.emailTitle)
                OutlinedTextField(
                    value = email,
                    onValueChange = { email = it },
                    placeholder = { Text(text = strings.emailHint) },
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("signup.email")
                        .semantics { contentDescription = strings.emailTitle },
                )
                Button(
                    onClick = { if (email.contains("@")) step = Step.Birthdate },
                    modifier = Modifier.testTag("signup.email.next").semantics { contentDescription = strings.emailNextCta },
                ) { Text(text = strings.emailNextCta) }
            }

            Step.Birthdate -> {
                Text(text = strings.birthdateTitle)
                OutlinedTextField(
                    value = birthdateInput,
                    onValueChange = { birthdateInput = it },
                    placeholder = { Text(text = strings.birthdateHint) },
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("signup.birthdate")
                        .semantics { contentDescription = strings.birthdateTitle },
                )
                Button(
                    onClick = {
                        val parsed = BirthdateValidator.parse(birthdateInput)
                        if (parsed != null) {
                            step = when (BirthdateValidator.evaluate(parsed)) {
                                AgeGateResult.Allowed -> Step.Avatar
                                AgeGateResult.RefusedUnder18 -> Step.AgeRefusalNeutral
                                AgeGateResult.RefusedCoppaUnder13 -> Step.AgeRefusalCoppa
                                // Future/invalid date: stay on the step so the user re-enters — never
                                // route a data error to an age-refusal state (SEC-S7-F1).
                                AgeGateResult.Invalid -> Step.Birthdate
                            }
                        }
                    },
                    modifier = Modifier.testTag("signup.birthdate.next").semantics { contentDescription = strings.birthdateNextCta },
                ) { Text(text = strings.birthdateNextCta) }
            }

            // Correction 1 — neutral-plain register (no candy, no mascot). Client-side attestation
            // only; server-side 18+ enforcement is S3/S18. Both refusals are dead ends by design (a
            // signup flow does not route an underage attestation anywhere else).
            Step.AgeRefusalNeutral -> StateView(
                title = strings.ageRefusalNeutralTitle,
                body = strings.ageRefusalNeutralBody,
                glyph = Glyph.DignityShield,
                register = Register.Neutral,
                testTag = "state.signup.age_refusal_neutral",
            )

            Step.AgeRefusalCoppa -> StateView(
                title = strings.ageRefusalCoppaTitle,
                body = strings.ageRefusalCoppaBody,
                glyph = Glyph.DignityShield,
                register = Register.Neutral,
                testTag = "state.signup.age_refusal_coppa",
            )

            Step.Avatar -> {
                Text(text = strings.avatarTitle)
                // DR-7.1: no OS permission asked here; skip is a live action (never a dead end, L6).
                TextButton(
                    onClick = { step = Step.Fandom },
                    modifier = Modifier.testTag("signup.avatar.skip").semantics { contentDescription = strings.avatarSkipCta },
                ) { Text(text = strings.avatarSkipCta) }
            }

            Step.Fandom -> {
                Text(
                    text = strings.fandomTitle,
                    modifier = Modifier.testTag("signup.fandom").semantics { contentDescription = strings.fandomTitle },
                )
                strings.fandomOptionLabels.forEachIndexed { index, label ->
                    TextButton(
                        onClick = { fandomTag = label },
                        modifier = Modifier
                            .testTag("signup.fandom.option.$index")
                            .semantics { contentDescription = label },
                    ) { Text(text = label) }
                }
                Button(
                    onClick = {
                        val tag = fandomTag ?: strings.fandomOptionLabels.firstOrNull() ?: ""
                        scope.launch {
                            val parsedBirthdate = BirthdateValidator.parse(birthdateInput)
                            if (parsedBirthdate != null) {
                                // S7 wires only UnavailableSignupGateway — this always resolves to the
                                // honest could-not-send end, never a fabricated success (L6).
                                gateway.submit(handle, email, parsedBirthdate, avatarRef = null, fandomTag = tag)
                            }
                            step = Step.CouldNotSend
                        }
                    },
                    modifier = Modifier.testTag("signup.submit").semantics { contentDescription = strings.submitCta },
                ) { Text(text = strings.submitCta) }
            }

            Step.CouldNotSend -> ProblemSurface.render(
                title = strings.couldNotSendTitle,
                body = strings.couldNotSendBody,
                messageKey = ProblemSurface.COULD_NOT_SEND_MESSAGE_KEY,
            )
        }
    }
}
