import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const [{ bookFileQualityRank, recordingQualityRank, formatRecordingQualityRank }] = loadFrontendModule(['Utilities/Quality/qualityRank.js']);

test('profile rank keeps zero, unranked and missing distinct without old global weight', () => {
  assert.equal(bookFileQualityRank({ qualityRank: 0, qualityWeight: 100 }), 0);
  assert.equal(bookFileQualityRank({ qualityRank: 0.5 }), 0.5);
  assert.equal(bookFileQualityRank({ qualityWeight: 100 }), -1);
  assert.equal(recordingQualityRank({ statistics: { fileQualityRank: 0 } }), 0);
  assert.equal(recordingQualityRank({ statistics: { fileQualityRank: -1 } }), -1);
  assert.equal(recordingQualityRank({ statistics: {} }), -2);
  assert.equal(formatRecordingQualityRank(0), '0%');
  assert.equal(formatRecordingQualityRank(-1), 'Unranked');
  assert.equal(formatRecordingQualityRank(null), 'No files');
  assert.equal(formatRecordingQualityRank(0.5), '50%');
});
