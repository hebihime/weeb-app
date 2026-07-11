// tools/egress-lint/egress-lint.test.mjs — golden vectors, red-fixture both directions (§12).
// Run: node --test tools/egress-lint/egress-lint.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  extractUrlHosts,
  isAllowedHost,
  isPackageHost,
  findDisallowedHosts,
  findTrackerDependencies,
  findManifestEgressHosts,
  isManifest,
  isRuntimeSource,
} from "./egress-lint.mjs";

test("extractUrlHosts: pulls hosts from http/https/ws/wss literals", () => {
  const c = `let a = "https://api.weeb.app/v1"; let b = "http://localhost:8080"; let w = "wss://x.evil.com/s"`;
  assert.deepEqual(extractUrlHosts(c), ["api.weeb.app", "localhost", "x.evil.com"]);
});

test("isAllowedHost: brand domains + subdomains + dev loopback pass; anything else fails", () => {
  assert.ok(isAllowedHost("weeb.app"));
  assert.ok(isAllowedHost("api.weeb.app"));
  assert.ok(isAllowedHost("friki.app"));
  assert.ok(isAllowedHost("cdn.friki.app"));
  assert.ok(isAllowedHost("localhost"));
  assert.ok(isAllowedHost("10.0.2.2"));
  assert.equal(isAllowedHost("evil.com"), false);
  assert.equal(isAllowedHost("weeb.app.evil.com"), false); // suffix-spoof must NOT pass
  assert.equal(isAllowedHost("firebaseio.com"), false);
});

test("GREEN: source that only calls brand + loopback hosts passes", () => {
  const files = [
    { path: "ios/ApiKit/Transport.swift", content: `let base = "https://api.weeb.app"` },
    { path: "ios/ApiKit/Debug.swift", content: `let dev = "http://localhost:8080"` },
  ];
  assert.deepEqual(findDisallowedHosts(files), []);
});

test("RED: a third-party host literal in source is caught", () => {
  const files = [{ path: "android/app/Analytics.kt", content: `val u = "https://api.mixpanel.com/track"` }];
  const v = findDisallowedHosts(files);
  assert.equal(v.length, 1);
  assert.match(v[0], /api\.mixpanel\.com/);
});

test("RED: suffix-spoofed brand host (weeb.app.evil.com) is caught", () => {
  const files = [{ path: "ios/X.swift", content: `let u = "https://weeb.app.evil.com/beacon"` }];
  assert.equal(findDisallowedHosts(files).length, 1);
});

test("GREEN: dependency manifest with only first-party + toolchain deps passes", () => {
  const files = [{
    path: "ios/Package.resolved",
    content: JSON.stringify({ pins: [{ identity: "swift-openapi-generator" }, { identity: "swift-openapi-runtime" }] }),
  }];
  assert.deepEqual(findTrackerDependencies(files), []);
});

test("RED: Firebase in a dependency manifest is caught", () => {
  const files = [{ path: "android/app/build.gradle.kts", content: `implementation("com.google.firebase:firebase-analytics:21.0.0")` }];
  const v = findTrackerDependencies(files);
  assert.ok(v.some((x) => x.includes("firebase")));
});

test("RED: Crashlytics + Sentry both flagged", () => {
  const files = [{ path: "ios/Package.swift", content: `.package(url: "https://github.com/getsentry/sentry-cocoa"), .package(name: "Crashlytics")` }];
  const v = findTrackerDependencies(files);
  assert.ok(v.some((x) => x.includes("sentry")));
  assert.ok(v.some((x) => x.includes("crashlytics")));
});

// ---------- manifest vs runtime-source classification (the Package.swift/gradle false-positive fix) ----------

test("isManifest / isRuntimeSource: Package.swift + *.gradle.kts are manifests, not runtime source", () => {
  assert.ok(isManifest("Package.swift"));
  assert.ok(isManifest("Package.resolved"));
  assert.ok(isManifest("build.gradle.kts"));
  assert.ok(isManifest("settings.gradle.kts"));
  assert.equal(isRuntimeSource("Package.swift"), false);   // NOT scanned by the runtime-host allowlist
  assert.equal(isRuntimeSource("build.gradle.kts"), false);
  assert.ok(isRuntimeSource("ContentView.swift"));
  assert.ok(isRuntimeSource("MainActivity.kt"));
});

test("a legit github SPM/plugin URL in a manifest is NOT a runtime-egress violation (tracker-scan only)", () => {
  const pkg = { path: "ios/ApiKit/Package.swift", content: `.package(url: "https://github.com/apple/swift-openapi-generator", from: "1.0.0")` };
  // Package.swift is a manifest, so it is tracker-scanned (github/apple is not a tracker) — clean...
  assert.deepEqual(findTrackerDependencies([pkg]), []);
  // ...and it must NEVER be fed to the runtime-host allowlist scan (that would wrongly flag github.com).
  // The collector (isRuntimeSource) is what excludes it; assert the classifier that drives it.
  assert.equal(isRuntimeSource("Package.swift"), false);
});

// ---------- manifest egress-host scan: prod host smuggled into a build file (SEC-S7-F2) ----------

test("isPackageHost: package registries + source forges pass; app/prod hosts do not", () => {
  assert.ok(isPackageHost("github.com"));
  assert.ok(isPackageHost("raw.githubusercontent.com"));
  assert.ok(isPackageHost("repo.maven.apache.org"));
  assert.ok(isPackageHost("dl.google.com"));
  assert.equal(isPackageHost("api.evil.com"), false);
  assert.equal(isPackageHost("weeb.app"), false); // app host, handled by isAllowedHost, not a pkg host
  assert.equal(isPackageHost("github.com.evil.com"), false); // suffix-spoof must NOT pass
});

test("GREEN: a manifest referencing only package hosts + loopback passes the manifest egress scan", () => {
  const files = [
    { path: "ios/ApiKit/Package.swift", content: `.package(url: "https://github.com/apple/swift-openapi-runtime", from: "1.0.0")` },
    { path: "android/app/build.gradle.kts", content: `buildConfigField("String","API_BASE_URL","\\"http://10.0.2.2:8080\\"")` },
    { path: "ios/Package.resolved", content: `{"location":"https://github.com/mattpolzin/OpenAPIKit"}` },
  ];
  assert.deepEqual(findManifestEgressHosts(files), []);
});

test("RED: a prod/egress host hardcoded in a .gradle.kts is caught by the manifest egress scan (SEC-S7-F2)", () => {
  const files = [{ path: "android/app/build.gradle.kts", content: `buildConfigField("String","API","\\"https://api.tracking-vendor.com/collect\\"")` }];
  const v = findManifestEgressHosts(files);
  assert.equal(v.length, 1);
  assert.match(v[0], /api\.tracking-vendor\.com/);
  // ...and Scan 1 (runtime source) would MISS it because a .gradle.kts is not runtime source.
  assert.equal(isRuntimeSource("build.gradle.kts"), false);
});

test("RED: an app-allowlisted host in a manifest is fine; a look-alike prod host is not", () => {
  const ok = [{ path: "x/build.gradle.kts", content: `val u = "https://api.weeb.app/v1"` }];
  assert.deepEqual(findManifestEgressHosts(ok), []);
  const bad = [{ path: "x/build.gradle.kts", content: `val u = "https://weeb.app.evil.com/beacon"` }];
  assert.equal(findManifestEgressHosts(bad).length, 1);
});
