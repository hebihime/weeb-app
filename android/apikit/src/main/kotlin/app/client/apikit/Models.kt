package app.client.apikit

import kotlinx.serialization.Serializable

/**
 * Hand-written mirrors of the exact shapes contracts/openapi.v0.json declares for the ONLY two
 * consumed operations (§1d). NOT hand-rolled REQUEST models (the contract has none — both endpoints are
 * bodiless GETs; the no-hand-rolled-request-model lint (§9f) is about request bodies, which do not
 * exist here). These are read/response models only, kept intentionally tiny and independent of the
 * generated client's exact class names (openapi-generator's tag-based operation-class naming is not
 * part of this contract's stable surface; the SCHEMA names are, so Transport decodes directly into
 * these instead of guessing a generated wrapper class name) — [ClientConfigResponse] and [Problem] are
 * value-for-value identical to the generated `app.client.apikit.generated.model` classes, verified by
 * ContractShapeTest against the same contracts/openapi.v0.json at test time.
 */
@Serializable
data class ClientConfigResponse(
    val apiVersion: String,
    val locales: List<String>,
    val defaultLocale: String,
)

@Serializable
data class HealthStatus(
    val status: String,
    val checkedAt: String,
)

@Serializable
data class Problem(
    val type: String,
    val title: String,
    val messageKey: String,
    val correlationId: String,
    val detail: String? = null,
    val instance: String? = null,
)

@Serializable
data class LimitReached(
    val quotaKey: String,
    val messageKey: String,
    val resetsAt: String,
    val premiumExtends: Boolean,
)
