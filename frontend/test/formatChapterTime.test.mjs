import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { default: formatChapterTime } = loadFrontendModule('Book/Details/formatChapterTime');

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
