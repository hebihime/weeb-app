#!/usr/bin/env node
// tools/egress-lint/egress-lint.mjs — SLICE_S7_CONTRACT.md §9f (client half of L20/L21).
//
// Zero third-party egress, enforced. Two scans over both client trees:
//   1. RUNTIME host literals in source (.swift/.kt): every http(s)/ws(s) URL host must be on the
//      allowlist — the brand domains (weeb.app / friki.app and subdomains) + dev loopback hosts
//      (localhost / 127.0.0.1 / 10.0.2.2, cleartext only in DEBUG configs). Any other host is egress.
//   2. DEPENDENCY manifests (Package.resolved / Package.swift / *.gradle* / gradle lockfiles): no
//      known analytics / crash-telemetry SDK (Firebase, Crashlytics, Sentry, Amplitude, …). Crash
//      telemetry is platform-native opt-in only; fonts are bundled, never fetched.
//
// Build-failing, red-fixture-proven both directions. Guarded activation (green until ios/android land).
//
// Usage: node tools/egress-lint/egress-lint.mjs [--repo-root <path>]   (exit 0 pass / 1 fail)

import { readFileSync, existsSync, readdirSync, statSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join, extname } from "node:path";

// Runtime egress allowlist. Domains match themselves + any subdomain; exact hosts match verbatim.
export const ALLOW_DOMAINS = ["weeb.app", "friki.app"];
export const ALLOW_EXACT = ["localhost", "127.0.0.1", "10.0.2.2", "0.0.0.0"];

// Known third-party analytics / crash / attribution SDKs — the §9f threat. Case-insensitive substring.
export const TRACKER_DENYLIST = [
  "firebase", "crashlytics", "google-analytics", "googleanalytics", "google/analytics",
  "sentry", "amplitude", "mixpanel", "segment.io", "segment.com", "com.segment",
  "bugsnag", "appcenter", "app-center", "flurry", "adjust.com", "com.adjust",
  "facebook", "fabric.io", "datadoghq", "datadog", "newrelic", "new-relic",
  "instabug", "branch.io", "io.branch", "onesignal", "appsflyer", "com.google.gms",
];

const URL_RE = /\b(?:https?|wss?):\/\/([a-zA-Z0-9._-]+)/g;

/** Every host referenced by a URL literal in the content. */
export function extractUrlHosts(content) {
  const hosts = [];
  let m;
  const re = new RegExp(URL_RE.source, URL_RE.flags);
  while ((m = re.exec(content)) !== null) hosts.push(m[1].toLowerCase().replace(/[.:]$/, ""));
  return hosts;
}

export function isAllowedHost(host) {
  if (ALLOW_EXACT.includes(host)) return true;
  return ALLOW_DOMAINS.some((d) => host === d || host.endsWith("." + d));
}

/** Scan 1: disallowed runtime hosts. files: [{path, content}]. */
export function findDisallowedHosts(files) {
  const violations = [];
  for (const { path, content } of files) {
    for (const host of extractUrlHosts(content)) {
      if (!isAllowedHost(host)) {
        violations.push(`${path}: egress host "${host}" not on the allowlist (weeb.app / friki.app / dev-loopback only) — no third-party egress (§9f)`);
      }
    }
  }
  return violations;
}

/** Scan 2: tracker SDKs in dependency manifests. files: [{path, content}]. */
export function findTrackerDependencies(files) {
  const violations = [];
  for (const { path, content } of files) {
    const lower = content.toLowerCase();
    for (const tracker of TRACKER_DENYLIST) {
      if (lower.includes(tracker)) {
        violations.push(`${path}: references analytics/telemetry SDK "${tracker}" — banned (§9f: no Firebase/Crashlytics/analytics; platform-native crash opt-in only)`);
      }
    }
  }
  return violations;
}

// ---------- file collection ----------

const SKIP_DIRS = new Set([".git", ".build", "build", "DerivedData", "node_modules", ".gradle", "Pods"]);
const SOURCE_EXT = new Set([".swift", ".kt", ".kts"]);
const MANIFEST_NAMES = new Set(["Package.resolved", "Package.swift"]);
const MANIFEST_EXT = new Set([".gradle", ".lockfile"]);

function walk(dir, pick, acc) {
  let entries;
  try { entries = readdirSync(dir); } catch { return acc; }
  for (const name of entries) {
    const full = join(dir, name);
    let s;
    try { s = statSync(full); } catch { continue; }
    if (s.isDirectory()) {
      if (!SKIP_DIRS.has(name)) walk(full, pick, acc);
    } else if (pick(name, full)) {
      acc.push({ path: full, content: readFileSync(full, "utf8") });
    }
  }
  return acc;
}

export function collectSourceFiles(root) {
  return walk(root, (name) => SOURCE_EXT.has(extname(name)), []);
}
export function collectManifestFiles(root) {
  return walk(root, (name) => MANIFEST_NAMES.has(name) || MANIFEST_EXT.has(extname(name)) || name.endsWith(".gradle.kts"), []);
}

async function main() {
  const args = process.argv.slice(2);
  const idx = args.indexOf("--repo-root");
  const __dirname = dirname(fileURLToPath(import.meta.url));
  const repoRoot = idx >= 0 ? args[idx + 1] : join(__dirname, "..", "..");

  const violations = [];
  let scanned = 0;
  for (const tree of ["ios", "android"]) {
    const root = join(repoRoot, tree);
    if (!existsSync(root)) {
      console.log(`egress-lint: ${tree}/ not present yet — egress scan guarded (arms at S7)`);
      continue;
    }
    const src = collectSourceFiles(root);
    const manifests = collectManifestFiles(root);
    scanned += src.length + manifests.length;
    violations.push(...findDisallowedHosts(src));
    violations.push(...findTrackerDependencies(manifests));
  }

  if (violations.length > 0) {
    console.error("egress-lint BLOCKED:");
    for (const v of violations) console.error(`  - ${v}`);
    process.exitCode = 1;
    return;
  }
  console.log(`egress-lint OK: ${scanned} client file(s) scanned; zero disallowed egress, zero tracker SDKs`);
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}
