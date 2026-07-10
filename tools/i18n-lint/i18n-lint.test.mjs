// tools/i18n-lint/i18n-lint.test.mjs — golden-vector tests. Run: node --test tools/i18n-lint/i18n-lint.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  checkKeyParity,
  findHardcodedStrings,
  checkBrandOverrideParity,
  checkStoreMetadataCoverage,
  loadLocales,
} from "./i18n-lint.mjs";

const LOCALES = ["en", "es", "pt", "zh-Hans"];

test("checkKeyParity: golden PASS — all four locales carry all keys", () => {
  const catalogs = {
    en: { welcome: "Welcome", cta: "Start" },
    es: { welcome: "Bienvenido", cta: "Empezar" },
    pt: { welcome: "Bem-vindo", cta: "Começar" },
    "zh-Hans": { welcome: "欢迎", cta: "开始" },
  };
  assert.deepEqual(checkKeyParity(catalogs, LOCALES), []);
});

test("checkKeyParity: golden FAIL — zh-Hans missing a key fails the build", () => {
  const catalogs = {
    en: { welcome: "Welcome", cta: "Start" },
    es: { welcome: "Bienvenido", cta: "Empezar" },
    pt: { welcome: "Bem-vindo", cta: "Começar" },
    "zh-Hans": { welcome: "欢迎" },
  };
  const violations = checkKeyParity(catalogs, LOCALES);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /key "cta" missing from locale "zh-Hans"/);
});

test("checkKeyParity: extra key in a non-base locale also fails (drift both directions)", () => {
  const catalogs = {
    en: { welcome: "Welcome" },
    es: { welcome: "Bienvenido", extra_es_only: "??" },
    pt: { welcome: "Bem-vindo" },
    "zh-Hans": { welcome: "欢迎" },
  };
  const violations = checkKeyParity(catalogs, LOCALES);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /extra_es_only.*does not exist in base locale/);
});

test("checkKeyParity: entirely missing locale catalog reported distinctly", () => {
  const catalogs = { en: { welcome: "Welcome" }, es: { welcome: "Bienvenido" }, pt: { welcome: "Bem-vindo" } };
  const violations = checkKeyParity(catalogs, LOCALES);
  assert.ok(violations.some((v) => v.includes('locale "zh-Hans" has no catalog at all')));
});

test("findHardcodedStrings: golden FAIL — SwiftUI literal Text(\"...\")", () => {
  const files = [{ path: "ios/Views/Onboarding.swift", content: 'VStack { Text("Welcome to Weeb!") }' }];
  const violations = findHardcodedStrings(files, { platform: "swift" });
  assert.equal(violations.length, 1);
  assert.match(violations[0], /Welcome to Weeb!/);
});

test("findHardcodedStrings: golden PASS — keyed string via LocalizedStringKey helper, no literal Text()", () => {
  const files = [{ path: "ios/Views/Onboarding.swift", content: "VStack { Text(L10n.welcome) }" }];
  assert.deepEqual(findHardcodedStrings(files, { platform: "swift" }), []);
});

test("findHardcodedStrings: allowlisted file is skipped", () => {
  const files = [{ path: "ios/Debug/DebugOverlay.swift", content: 'Text("DEBUG ONLY")' }];
  const violations = findHardcodedStrings(files, { platform: "swift", allowlist: new Set(["ios/Debug/DebugOverlay.swift"]) });
  assert.deepEqual(violations, []);
});

test("findHardcodedStrings: unknown platform throws", () => {
  assert.throws(() => findHardcodedStrings([], { platform: "cobol" }), /unknown platform/);
});

test("checkBrandOverrideParity: golden FAIL — friki override missing while weeb has one", () => {
  const base = { streak_toast: "You're on a streak!" };
  const overrides = { weeb: { streak_toast: "Sugoi streak!" }, friki: {} };
  const violations = checkBrandOverrideParity(base, overrides, ["weeb", "friki"]);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /not for "friki"/);
});

test("checkBrandOverrideParity: golden FAIL — override key not present in base catalog", () => {
  const base = {};
  const overrides = { weeb: { ghost_key: "x" }, friki: { ghost_key: "y" } };
  const violations = checkBrandOverrideParity(base, overrides, ["weeb", "friki"]);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /no base-catalog entry/);
});

test("checkBrandOverrideParity: golden PASS — symmetric overrides for both brands", () => {
  const base = { streak_toast: "You're on a streak!" };
  const overrides = { weeb: { streak_toast: "Sugoi streak!" }, friki: { streak_toast: "Racha épica!" } };
  assert.deepEqual(checkBrandOverrideParity(base, overrides, ["weeb", "friki"]), []);
});

test("checkStoreMetadataCoverage: golden FAIL — EN-only listing fails that brand's leg", () => {
  const present = { "weeb/ios": ["en"], "weeb/android": ["en", "es", "pt", "zh-Hans"] };
  const violations = checkStoreMetadataCoverage(present, LOCALES, ["weeb/ios", "weeb/android"]);
  assert.equal(violations.length, 3); // weeb/ios missing es, pt, zh-Hans
  assert.ok(violations.every((v) => v.includes("weeb/ios")));
});

test("checkStoreMetadataCoverage: golden PASS — all four listings, all four locales", () => {
  const listings = ["weeb/ios", "weeb/android", "friki/ios", "friki/android"];
  const present = Object.fromEntries(listings.map((l) => [l, LOCALES]));
  assert.deepEqual(checkStoreMetadataCoverage(present, LOCALES, listings), []);
});

test("loadLocales: reads the canonical i18n/locales.json set", () => {
  const repoRoot = new URL("../..", import.meta.url).pathname;
  const locales = loadLocales(repoRoot);
  assert.deepEqual(locales, LOCALES);
});
