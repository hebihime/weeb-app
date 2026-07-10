namespace Svac.DomainCore.Contracts;

/// <summary>
/// Marks a type as a DevSeams-only implementation (SLICE_S1_CONTRACT.md §1b, §12.16): a fake
/// payment/crypto/region/con backend usable only when the DevSeams environment flag is set. DevSeams is
/// an environment/deployment flag, NEVER a 9A entry — a runtime-tunable that swaps these in from the ops
/// desk must be structurally impossible. An arch test scans prod DI composition for any type carrying
/// this attribute and fails the build if one is found registered outside a DevSeams-gated branch.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DevSeamsOnlyAttribute : Attribute;
