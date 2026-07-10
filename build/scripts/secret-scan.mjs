#!/usr/bin/env node
// build/scripts/secret-scan.mjs — scan a unified diff (stdin) for secret patterns.
// Usage: git diff --cached -U0 | node build/scripts/secret-scan.mjs
//
// Replaces the inline `git diff | grep -qE '...'` in the pre-commit hook and lints.yml.
// Two real failures this design removes (both shipped, both found at S0):
//   1. FAIL-OPEN under pipefail: `grep -q` exits on first match, `git diff` takes SIGPIPE(141),
//      `set -o pipefail` turns the MATCH into a nonzero pipeline status, and the `if` that should
//      block sees false. Triggered only on diffs large enough that git is still writing when grep
//      quits — i.e. it failed open precisely on big commits. This script reads stdin to EOF; the
//      race is structurally gone.
//   2. SELF-MATCH: the pattern text lived inline in lints.yml, so the commit ADDING the scanner
//      tripped it. Patterns live in secret-patterns.txt, which this scanner skips (a regex is not
//      a secret); no scannable file contains pattern literals.
//
// Scans ADDED lines only ('+' lines): removed lines are already in history; blocking their
// removal would punish cleanup.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
export const PATTERNS_PATH = join(HERE, 'secret-patterns.txt');
const EXCLUDED_FILE = 'build/scripts/secret-patterns.txt';

export function loadPatterns(path = PATTERNS_PATH) {
  return readFileSync(path, 'utf8')
    .split('\n')
    .map(l => l.trim())
    .filter(l => l && !l.startsWith('#'))
    .map(src => new RegExp(src));
}

// Returns findings: [{file, line, masked}]. `diff` is unified-diff text.
export function scan(diff, patterns) {
  const findings = [];
  let file = null;
  for (const line of diff.split('\n')) {
    if (line.startsWith('+++ ')) {
      // "+++ b/path" (or "+++ /dev/null" on delete)
      file = line.startsWith('+++ b/') ? line.slice(6) : null;
      continue;
    }
    if (!line.startsWith('+') || line.startsWith('+++')) continue;
    if (file === EXCLUDED_FILE) continue;
    const added = line.slice(1);
    for (const re of patterns) {
      const m = re.exec(added);
      if (m) {
        const at = added.indexOf(m[0]);
        findings.push({
          file: file ?? '(unknown file)',
          masked: added.slice(Math.max(0, at - 10), at + 6) + '…(masked)',
        });
        break;
      }
    }
  }
  return findings;
}

const isMain = process.argv[1] === fileURLToPath(import.meta.url);
if (isMain) {
  const diff = readFileSync(0, 'utf8'); // stdin to EOF — no early-exit fail-open possible
  const findings = scan(diff, loadPatterns());
  if (findings.length > 0) {
    for (const f of findings) console.error(`secret-scan BLOCKED: ${f.file}: …${f.masked}`);
    process.exit(1);
  }
  console.log('secret-scan OK');
}
