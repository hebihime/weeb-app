// build/scripts/merge-purge-registry.test.mjs — golden vectors for the purge-registry fragment merger
// (SLICE_S5_CONTRACT.md §6). Run: node --test build/scripts/merge-purge-registry.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { mergeRegistries } from "./merge-purge-registry.mjs";

const coreFragment = [
  { storeKey: "config_entries", purgeClass: "AccountDeletion", verb: "NotApplicable", reason: "zero personal data" },
];
const identityFragment = [
  { storeKey: "identity.accounts", purgeClass: "AccountDeletion", verb: "Tombstone", reason: "PII columns -> NULL/sentinel" },
];
const adminFragment = [
  { storeKey: "admin.staff_accounts", purgeClass: "AccountDeletion", verb: "NotApplicable", reason: "staff are not consumer accounts" },
  { storeKey: "admin.staff_role_grants", purgeClass: "AccountDeletion", verb: "NotApplicable", reason: "staff are not consumer accounts" },
];

test("merges distinct fragments by concatenation, in argument order (never re-sorted)", () => {
  const merged = mergeRegistries([coreFragment, identityFragment, adminFragment]);
  assert.equal(merged.length, 4);
  assert.deepEqual(
    merged.map((e) => e.storeKey),
    ["config_entries", "identity.accounts", "admin.staff_accounts", "admin.staff_role_grants"]
  );
});

test("red fixture: the same (storeKey, purgeClass) cell claimed by two fragments throws", () => {
  const colliding = [{ storeKey: "identity.accounts", purgeClass: "AccountDeletion", verb: "NotApplicable", reason: "fixture collision" }];
  assert.throws(
    () => mergeRegistries([identityFragment, colliding]),
    /identity\.accounts, AccountDeletion.*more than one fragment/s
  );
});

test("a single fragment merges to itself, in its own original order", () => {
  const merged = mergeRegistries([adminFragment]);
  assert.deepEqual(merged, adminFragment);
});

test("zero fragments merges to an empty array", () => {
  assert.deepEqual(mergeRegistries([]), []);
});

test("distinct purge classes on the SAME store key across fragments do not collide", () => {
  const a = [{ storeKey: "admin.staff_accounts", purgeClass: "AccountDeletion", verb: "NotApplicable", reason: "r1" }];
  const b = [{ storeKey: "admin.staff_accounts", purgeClass: "StatutoryErasure", verb: "Pseudonymize", reason: "r2" }];
  const merged = mergeRegistries([a, b]);
  assert.equal(merged.length, 2);
});
