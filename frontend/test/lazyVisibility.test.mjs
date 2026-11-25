import assert from 'node:assert/strict';
import test from 'node:test';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { default: RevealWhenVisible, observeVisibility } = loadFrontendModule('Components/Image/RevealWhenVisible');

test('the initial markup keeps the placeholder and does not request the real image', () => {
  const markup = renderToStaticMarkup(React.createElement(RevealWhenVisible, {
    placeholder: React.createElement('img', { src: 'placeholder.png' }),
    children: React.createElement('img', { src: 'cover.jpg' })
  }));
  assert.match(markup, /placeholder\.png/);
  assert.doesNotMatch(markup, /cover\.jpg/);
});

test('intersection reveals only the correct image once and disconnects observers', () => {
  const element = {};
  let callback;
  let observed;
  let options;
  let disconnects = 0;
  let revealed = 0;
  const stop = observeVisibility(element, () => revealed++, (report, config) => {
    callback = report;
    options = config;
    return {
      observe(node) { observed = node; },
      disconnect() { disconnects++; }
    };
  }, true);
  assert.equal(observed, element);
  assert.deepEqual(options, { rootMargin: '100px', scrollMargin: '100px' });
  callback([{ target: {}, isIntersecting: true }, { target: element, isIntersecting: false }]);
  assert.equal(revealed, 0);
  callback([{ target: element, isIntersecting: true }]);
  assert.equal(revealed, 1);
  callback([{ target: element, isIntersecting: false }, { target: element, isIntersecting: true }]);
  stop();
  assert.equal(revealed, 1);
  assert.equal(disconnects, 1);
});

test('unmount ignores delayed observer events, and unsupported browsers load immediately', () => {
  const element = {};
  let callback;
  let disconnected = false;
  let revealed = 0;
  const stop = observeVisibility(element, () => revealed++, report => {
    callback = report;
    return { observe() {}, disconnect() { disconnected = true; } };
  }, false);
  stop();
  callback([{ target: element, isIntersecting: true }]);
  assert.equal(revealed, 0);
  assert.equal(disconnected, true);
  const noObserver = observeVisibility(element, () => revealed++, undefined, false);
  assert.equal(revealed, 1);
  noObserver();
});
