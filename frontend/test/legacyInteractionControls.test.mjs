import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const size = loadFrontendModule('Settings/Quality/Definition/qualitySizeSlider.ts');
const { default: TextTruncate } = loadFrontendModule('Components/TextTruncate.tsx');

test('quality slider maps both unlimited aliases and finite limits without rewriting the untouched thumb', () => {
  for (const maxSize of [null, undefined, 0]) {
    const previous = { minSize: 20.4, maxSize };
    const [min, max] = size.sliderValues(previous);
    assert.equal(max, size.SLIDER_MAX);
    assert.deepEqual(size.sizesFromSlider([min + 0.1, max], previous), {
      minSize: Math.round(Math.pow(min + 0.1, 1.1) * 10) / 10,
      maxSize
    });
  }
  const previous = { minSize: 20.4, maxSize: 91.7 };
  const [min, max] = size.sliderValues(previous);
  assert.deepEqual(size.sizesFromSlider([min, max], previous), previous);
  assert.deepEqual(size.sizesFromSlider([min, size.SLIDER_MAX], previous), { minSize: 20.4, maxSize: null });
});

test('slider rejects malformed values and preserves smaller legal numeric input gaps', () => {
  const previous = { minSize: 20, maxSize: 21 };
  const values = size.sliderValues(previous);
  assert.ok(size.sliderStepsBetween(values) < 50);
  const [[minLow, minHigh], [maxLow, maxHigh]] = size.sliderThumbBounds([20, 40]);
  assert.deepEqual([minLow, minHigh, maxLow, maxHigh], [0, 35, 25, size.SLIDER_MAX]);
  assert.deepEqual(size.sliderThumbBounds(values), [[0, values[0]], [values[1], size.SLIDER_MAX]]);
  assert.equal(size.sliderStepsBetween([0, size.SLIDER_MAX]), 50);
  for (const malformed of [[1], [1, NaN], [5, 3], [0, size.SLIDER_MAX + 1]]) {
    assert.equal(size.sizesFromSlider(malformed, previous), null);
  }
  assert.equal(size.normalizeSize(-1), 0);
  assert.equal(size.normalizeSize(NaN), 0);
  assert.equal(size.normalizeSize(Infinity), 0);
  assert.equal(size.normalizeSize(1600), 1500);
});

test('text truncation uses native clamp and retains complete Unicode text', () => {
  const full = 'An audiobook overview — 物語 ' + 'and the rest of the original text. '.repeat(8);
  const element = TextTruncate({ text: full, line: 3.9 });
  assert.equal(element.props.style.WebkitLineClamp, 3);
  assert.equal(element.props.style.overflow, 'hidden');
  assert.equal(element.props.children, full);
  assert.equal(TextTruncate({ text: full, line: 0 }), null);
  assert.equal(TextTruncate({ text: null, line: 3 }), null);
});
