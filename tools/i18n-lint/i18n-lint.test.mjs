// tools/i18n-lint/i18n-lint.test.mjs — golden-vector tests. Run: node --test tools/i18n-lint/i18n-lint.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  checkKeyParity,
  findHardcodedStrings,
  checkBrandOverrideParity,
  checkStoreMetadataCoverage,
  loadLocales,
  parseXcstrings,
  parseAndroidStrings,
  androidResName,
  checkMessageKeyCoverage,
  collectRazorFiles,
} from "./i18n-lint.mjs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..", "..");

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

// ---------- S7 native catalog parsers ----------

const XCSTRINGS = JSON.stringify({
  sourceLanguage: "en",
  version: "1.0",
  strings: {
    "signup.handle.title": {
      localizations: {
        en: { stringUnit: { state: "translated", value: "Pick a handle" } },
        es: { stringUnit: { value: "Elige un usuario" } },
        pt: { stringUnit: { value: "Escolha um nome" } },
        "zh-Hans": { stringUnit: { value: "选择用户名" } },
      },
    },
    "limit_reached.generic": {
      localizations: {
        en: { stringUnit: { value: "You've hit today's limit." } },
        es: { stringUnit: { value: "Has alcanzado el límite de hoy." } },
        pt: { stringUnit: { value: "Você atingiu o limite de hoje." } },
        "zh-Hans": { stringUnit: { value: "已达到今日上限。" } },
      },
    },
  },
});

test("parseXcstrings: builds per-locale catalogs from a String Catalog", () => {
  const cats = parseXcstrings(XCSTRINGS, LOCALES);
  assert.equal(cats.en["signup.handle.title"], "Pick a handle");
  assert.equal(cats["zh-Hans"]["limit_reached.generic"], "已达到今日上限。");
  assert.deepEqual(Object.keys(cats.es).sort(), ["limit_reached.generic", "signup.handle.title"]);
});

test("parseXcstrings: a locale missing a value is flagged by checkKeyParity", () => {
  const doc = JSON.parse(XCSTRINGS);
  delete doc.strings["signup.handle.title"].localizations["pt"];
  const cats = parseXcstrings(JSON.stringify(doc), LOCALES);
  const v = checkKeyParity(cats, LOCALES);
  assert.ok(v.some((x) => x.includes('"signup.handle.title"') && x.includes('"pt"')));
});

test("parseAndroidStrings + androidResName: dotted key maps to underscore resource", () => {
  const xml = `<resources>
    <string name="signup_handle_title">Pick a handle</string>
    <string name="limit_reached_generic">You've hit today's limit.</string>
  </resources>`;
  const cat = parseAndroidStrings(xml);
  assert.equal(cat["signup_handle_title"], "Pick a handle");
  assert.equal(androidResName("limit_reached.generic"), "limit_reached_generic");
  assert.ok(androidResName("limit_reached.generic") in cat);
});

test("checkMessageKeyCoverage: GREEN — all message keys present ×4 in both platforms", () => {
  const iosCats = parseXcstrings(XCSTRINGS, LOCALES);
  const androidCats = {};
  for (const loc of LOCALES) androidCats[loc] = { signup_handle_title: "x", limit_reached_generic: "y" };
  const v = checkMessageKeyCoverage(
    [{ platform: "ios", catalogs: iosCats }, { platform: "android", catalogs: androidCats }],
    ["limit_reached.generic"],
    LOCALES
  );
  assert.deepEqual(v, []);
});

test("RED: a message key missing from one iOS locale is caught", () => {
  const doc = JSON.parse(XCSTRINGS);
  delete doc.strings["limit_reached.generic"].localizations["zh-Hans"];
  const iosCats = parseXcstrings(JSON.stringify(doc), LOCALES);
  const v = checkMessageKeyCoverage([{ platform: "ios", catalogs: iosCats }], ["limit_reached.generic"], LOCALES);
  assert.ok(v.some((x) => x.includes("limit_reached.generic") && x.includes('"zh-Hans"')));
});

test("RED: a message key missing its underscored Android name is caught", () => {
  const androidCats = {};
  for (const loc of LOCALES) androidCats[loc] = { signup_handle_title: "x" }; // no limit_reached_generic
  const v = checkMessageKeyCoverage([{ platform: "android", catalogs: androidCats }], ["limit_reached.generic"], LOCALES);
  assert.equal(v.length, LOCALES.length);
  assert.ok(v.every((x) => x.includes("android") && x.includes("limit_reached_generic")));
});

test("checkMessageKeyCoverage: an absent platform (empty catalogs) is skipped, not failed", () => {
  const empty = Object.fromEntries(LOCALES.map((l) => [l, {}]));
  assert.deepEqual(checkMessageKeyCoverage([{ platform: "ios", catalogs: empty }], ["limit_reached.generic"], LOCALES), []);
});

test("collectRazorFiles: finds the real admin-host .razor files under backend/admin-host", () => {
  const files = collectRazorFiles(repoRoot);
  const relPaths = files.map((f) => f.path.replace(repoRoot, "").replace(/\\/g, "/"));

  assert.ok(relPaths.some((p) => p.endsWith("Components/App.razor")));
  assert.ok(relPaths.some((p) => p.endsWith("Components/Pages/SignIn.razor")));
  assert.ok(relPaths.some((p) => p.endsWith("Components/Pages/Dashboard.razor")));
});

test("SLICE_S5_CONTRACT.md §8 seam 14: the REAL shipped admin-host .razor files carry zero hardcoded literals", () => {
  // Every text node in these files renders through @AdminStringCatalog (an @-expression, never a bare
  // literal) — this is the standing, permanent regression proof over the committed files themselves.
  const files = collectRazorFiles(repoRoot);
  assert.ok(files.length >= 3, "expected at least the three scaffold .razor files to be found");

  const violations = findHardcodedStrings(files, { platform: "razor" });
  assert.deepEqual(violations, []);
});

test("RED: an admin-host .razor file with a bare literal IS caught by the razor platform pattern", () => {
  const fixture = [{ path: "Fixture.razor", content: "<h1>Hardcoded literal test</h1>" }];
  const violations = findHardcodedStrings(fixture, { platform: "razor" });
  assert.equal(violations.length, 1);
  assert.ok(violations[0].includes("Hardcoded literal test"));
});
