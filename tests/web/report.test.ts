import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {parseReport, duration, percent} from '../../src/web/src/report.ts';

const fixture = () => JSON.parse(readFileSync(new URL('../../fixtures/daily-report.json', import.meta.url), 'utf8'));

test('accepts the shared synthetic report and formats measures', () => {
  const report = parseReport(fixture(), '2026-09-13');
  assert.equal(report.synthetic, true);
  assert.equal(report.employees[0].categories.unknown_seconds, 600);
  assert.equal(duration(3661), '1h 1m');
  assert.equal(percent(5 / 6), '83.3%');
  assert.equal(percent(null), 'Unavailable');
});

test('rejects category totals that do not reconcile', () => {
  const report = fixture();
  report.employees[0].categories.unknown_seconds = 0;
  assert.throws(() => parseReport(report, '2026-09-13'), /response is invalid/);
});

test('rejects unavailable values without a reason', () => {
  const report = fixture();
  report.employees[0].output.unavailable_reason = null;
  assert.throws(() => parseReport(report, '2026-09-13'), /response is invalid/);
});

test('rejects a response for a different team or date', () => {
  assert.throws(() => parseReport(fixture(), '2026-09-14'), /response is invalid/);
  assert.throws(() => parseReport(fixture(), '2026-09-13', 'another-team'), /response is invalid/);
});
