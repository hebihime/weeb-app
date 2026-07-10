// build/scripts/secret-scan.test.mjs — golden vectors for the secret scanner.
// Vectors 3 and 4 are the two REAL shipped bugs (S0): self-match and large-diff fail-open.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { scan, loadPatterns } from './secret-scan.mjs';

const patterns = loadPatterns();
const SCRIPT = fileURLToPath(new URL('./secret-scan.mjs', import.meta.url));

// Test vectors are CONCATENATED so this file's own committed source never contains a
// secret-shaped literal — otherwise the diff ADDING these tests trips the scanner (that
// exact self-match already shipped twice at S0: first in lints.yml, then in this file's
// first draft). The exemption list must stay at exactly one file: secret-patterns.txt.
const AWS_KEY = 'AKIA' + 'IOSFODNN7EXAMPLE';
const PRIV_KEY = '-----BEGIN RSA ' + 'PRIVATE KEY-----';
const STRIPE_KEY = 'sk_live_' + 'abcdefghijklmnopqrstu';
const AZURE_CONN = 'DefaultEndpointsProtocol=https;' + 'AccountName=';

const diffFor = (file, addedLines) =>
  `diff --git a/${file} b/${file}\n--- a/${file}\n+++ b/${file}\n@@ -0,0 +1 @@\n` +
  addedLines.map(l => '+' + l).join('\n') + '\n';

test('clean diff passes', () => {
  assert.equal(scan(diffFor('src/app.cs', ['var x = 1;']), patterns).length, 0);
});

test('each pattern class blocks: AWS key, private key, Stripe live, Azure conn string', () => {
  const vectors = [
    `const k = "${AWS_KEY}";`,
    PRIV_KEY,
    `stripe = "${STRIPE_KEY}";`,
    `conn = "${AZURE_CONN}prodacct";`,
  ];
  for (const v of vectors) {
    assert.equal(scan(diffFor('appsettings.json', [v]), patterns).length, 1, v);
  }
});

test('REGRESSION (self-match): pattern text inside secret-patterns.txt is exempt', () => {
  const d = diffFor('build/scripts/secret-patterns.txt', [AZURE_CONN]);
  assert.equal(scan(d, patterns).length, 0);
});

test('pattern text in any OTHER file still blocks (the exemption is one file, not a class)', () => {
  const d = diffFor('.github/workflows/lints.yml', [`grep '${AZURE_CONN}'`]);
  assert.equal(scan(d, patterns).length, 1);
});

test('REGRESSION (fail-open on large diff): secret early in a >1MB diff blocks via CLI', () => {
  // The old grep -q + pipefail pipeline failed OPEN here: match found early, writer SIGPIPEd,
  // pipeline status 141, if-branch skipped. The CLI must read all of stdin and exit 1.
  const filler = diffFor('big.txt', Array.from({ length: 20000 }, (_, i) => `filler line ${i} ${'x'.repeat(40)}`));
  const bad = diffFor('leak.cs', [`var k = "${AWS_KEY}";`]) + filler;
  assert.throws(
    () => execFileSync('node', [SCRIPT], { input: bad, stdio: ['pipe', 'pipe', 'pipe'] }),
    /status 1|Command failed/,
  );
});

test('removed lines never block (cleanup is not a leak)', () => {
  const d = `diff --git a/old.cs b/old.cs\n--- a/old.cs\n+++ b/old.cs\n@@ -1 +0,0 @@\n-var k = "${AWS_KEY}";\n`;
  assert.equal(scan(d, patterns).length, 0);
});

test('CLI passes a clean large diff (exit 0, prints OK)', () => {
  const filler = diffFor('big.txt', Array.from({ length: 5000 }, (_, i) => `line ${i}`));
  const out = execFileSync('node', [SCRIPT], { input: filler, encoding: 'utf8' });
  assert.match(out, /secret-scan OK/);
});
