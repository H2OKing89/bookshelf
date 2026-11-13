import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const source = await readFile(new URL('../src/Utilities/Book/recordingIdentity.js', import.meta.url), 'utf8');
const { getRecordingIdentity, summarizeSeriesRecordings } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

const recording = (id, asin, files = 0, region = 'us') => ({
  id,
  foreignBookId: `audible:${region}:${asin}`,
  statistics: { bookFileCount: files }
});

test('keeps UK and US recordings distinct even when their titles or series positions agree', () => {
  const uk = recording(1, 'B0F14RFHS6', 1, 'uk');
  const us = recording(2, 'B0F14RPXHR', 1);
  assert.deepEqual(getRecordingIdentity(uk.foreignBookId), { marketplace: 'UK', asin: 'B0F14RFHS6' });
  assert.deepEqual(getRecordingIdentity(us.foreignBookId), { marketplace: 'US', asin: 'B0F14RPXHR' });
  assert.deepEqual(summarizeSeriesRecordings([uk, us], { 1: '1', 2: '1' }), {
    recordingCount: 2, volumeCount: 1, ownedVolumeCount: 1, marketplaces: ['UK', 'US'], hasAlternates: true
  });
});

test('counts the twelve Harry Potter catalog recordings as seven volumes, all with files', () => {
  const items = [recording(1, 'B0F14RPXHR', 1), recording(2, 'B0F154ZCSN', 1)];
  const positions = { 1: '1', 2: '2' };
  const pairs = [
    ['B0F14PK33J', 'B0F14YFFHW'], ['B0F14PB6WN', 'B0F14RHNSH'],
    ['B0F14P2632', 'B0F14SH8SN'], ['B0F14KKPH6', 'B0F1554Y8N'],
    ['B0F14RVHWN', 'B0F14PB3DT']
  ];
  pairs.forEach((asins, index) => asins.forEach((asin, variant) => {
    const id = items.length + 1;
    positions[id] = String(index + 3);
    items.push(recording(id, asin, variant));
  }));
  const before = structuredClone(items);
  assert.deepEqual(summarizeSeriesRecordings(items, positions), {
    recordingCount: 12, volumeCount: 7, ownedVolumeCount: 7, marketplaces: ['US'], hasAlternates: true
  });
  assert.deepEqual(items, before);
});

test('normalizes numeric position spelling without merging or dropping recording rows', () => {
  const items = [recording(1, 'B0F14PK33J'), recording(2, 'B0F14YFFHW', 1)];
  const summary = summarizeSeriesRecordings(items, { 1: '03', 2: '3.0' });
  assert.equal(summary.volumeCount, 1);
  assert.equal(summary.recordingCount, 2);
  assert.equal(summary.ownedVolumeCount, 1);
});

test('does not infer volume counts from missing positions or omnibus ranges', () => {
  for (const position of [undefined, '', '1-3', 'One']) {
    const summary = summarizeSeriesRecordings([recording(1, 'B0F14PK33J')], { 1: position });
    assert.equal(summary.volumeCount, null);
    assert.equal(summary.ownedVolumeCount, null);
    assert.equal(summary.recordingCount, 1);
  }
});

test('does not claim file ownership while statistics are unavailable', () => {
  const book = recording(1, 'B0F14PK33J');
  delete book.statistics;
  assert.equal(summarizeSeriesRecordings([book], { 1: '3' }).ownedVolumeCount, null);
});

test('retains legacy book counts and rejects author identities or malformed recording IDs', () => {
  for (const id of [null, '', '12345', 'audible:us:name:J.K.%20Rowling', 'audible:us:B0F14PK33J:extra']) {
    assert.equal(getRecordingIdentity(id), null);
    assert.equal(summarizeSeriesRecordings([{ id: 1, foreignBookId: id }], { 1: '1' }), null);
  }
  assert.equal(summarizeSeriesRecordings([], {}), null);
});
