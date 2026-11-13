import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import test from 'node:test';
import { Script } from 'node:vm';

const require = createRequire(import.meta.url);
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const { transformSync } = require('@babel/core');
const helperSource = await readFile(new URL('../src/BookFile/formatMediaInfo.js', import.meta.url), 'utf8');
const helpers = await import(`data:text/javascript;base64,${Buffer.from(helperSource).toString('base64')}`);
const { default: formatMediaInfo, getMediaInfoDetails, getMediaInfoSource } = helpers;
const componentSource = await readFile(new URL('../src/BookFile/MediaInfo.js', import.meta.url), 'utf8');
const { code } = transformSync(componentSource, {
  configFile: false,
  babelrc: false,
  presets: [['@babel/preset-env', { targets: { node: 'current' } }], '@babel/preset-react']
});
const componentModule = { exports: {} };
const componentRequire = (name) => {
  if (name === './formatMediaInfo') {
    return { __esModule: true, ...helpers };
  }
  if (name === './mediaInfoTypes') {
    return { AUDIO: 'audio' };
  }
  if (name === './MediaInfo.css') {
    return {};
  }
  return require(name);
};
// Load the real JSX component while substituting only its CSS module for this Node test.
new Script(code).runInNewContext({ require: componentRequire, module: componentModule, exports: componentModule.exports });
const MediaInfo = componentModule.exports.default;

function render(mediaInfo) {
  return renderToStaticMarkup(React.createElement(MediaInfo, { type: 'audio', ...mediaInfo }));
}

test('formats actual codec labels and measurements without interpreting channel counts as Atmos', () => {
  assert.equal(formatMediaInfo({ audioCodec: 'USAC', audioChannels: 8, audioBitRate: '64 kbps', audioBitrateMode: 'VBR' }), 'USAC · 8 channels · 64 kbps (VBR)');
  assert.equal(formatMediaInfo({ audioCodec: 'AAC LC', audioChannels: 1, audioSampleRate: '44.1kHz' }), 'AAC LC · 1 channel · 44.1kHz');
  assert.equal(formatMediaInfo({ audioCodec: 'Unknown', audioFormat: 'A future codec' }), 'A future codec');
  assert.equal(formatMediaInfo({ audioCodec: 'Unknown', audioChannels: 0, audioBitRate: '0 kbps', audioSampleRate: '0kHz' }), '');
  assert.equal(formatMediaInfo(), '');
});

test('preserves explicit false and long runtimes and omits missing values in stream details', () => {
  const details = Object.fromEntries(getMediaInfoDetails({ durationSeconds: 223438.5, isDefault: false, language: 'eng' }));
  assert.equal(details.Duration, '62:03:58');
  assert.equal(details['Default stream'], 'No');
  assert.equal(details.Language, 'eng');
  assert.equal(details.Profile, undefined);
  for (const durationSeconds of [null, undefined, '', -1, NaN, Infinity]) {
    assert.equal(Object.fromEntries(getMediaInfoDetails({ durationSeconds })).Duration, undefined);
  }
});

test('keeps tracker reports distinct from locally read media information', () => {
  assert.equal(getMediaInfoSource({ source: 'MyAnonaMouse' }), 'Reported by MAM');
  assert.equal(getMediaInfoSource({ source: 'MediaInfo', sourceVersion: '26.10' }), 'Read from file · MediaInfo 26.10');
  assert.equal(getMediaInfoSource({ source: 'TagLib' }), 'Read from file · TagLib');
  assert.equal(getMediaInfoSource({}), '');
  const html = render({ source: 'MyAnonaMouse', audioCodec: 'AAC LC' });
  assert.match(html, /Reported by MAM/);
  assert.doesNotMatch(html, /Read from file/);
});

test('renders all audio streams behind a native keyboard accessible disclosure and escapes untrusted titles', () => {
  const html = render({
    source: 'MediaInfo',
    sourceVersion: '26.10',
    containerFormat: 'MPEG-4',
    audioCodec: 'xHE-AAC (USAC)',
    audioStreams: [
      { index: 0, audioCodec: 'xHE-AAC (USAC)', audioFormat: 'USAC', audioAdditionalFeatures: 'xHE-AAC', isDefault: true },
      { index: 1, audioCodec: 'Dolby Atmos (E-AC-3)', audioFormat: 'E-AC-3', title: '<script>bad()</script>', language: 'eng', isDefault: false }
    ]
  });
  assert.match(html, /<details/);
  assert.match(html, /<summary[^>]*>xHE-AAC \(USAC\)/);
  assert.match(html, /2 audio streams/);
  assert.match(html, /Audio stream 1 \(default\)/);
  assert.match(html, /Audio stream 2/);
  assert.match(html, /Dolby Atmos \(E-AC-3\)/);
  assert.match(html, /Container: MPEG-4/);
  assert.match(html, /&lt;script&gt;bad\(\)&lt;\/script&gt;/);
  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /Read from file · MediaInfo 26.10/);
});

test('omits an empty media-info block rather than presenting unavailable values as facts', () => {
  assert.equal(render({ audioCodec: 'Unknown', audioChannels: 0 }), '');
});
