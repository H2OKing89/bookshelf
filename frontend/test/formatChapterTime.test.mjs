import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const source = await readFile(new URL('../src/Book/Details/formatChapterTime.js', import.meta.url), 'utf8');
const { default: formatChapterTime } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

test('shows chapter offsets without wrapping long recordings at 24 hours', () => {
  assert.equal(formatChapterTime(0), '0:00:00');
  assert.equal(formatChapterTime(61199), '0:01:01');
  assert.equal(formatChapterTime(223438000), '62:03:58');
});

test('omits missing or invalid timings instead of displaying a misleading zero', () => {
  for (const value of [null, undefined, '', '61000', -1, NaN, Infinity]) {
    assert.equal(formatChapterTime(value), '');
  }
});
