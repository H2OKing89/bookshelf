import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const [{ bookFileQualityRank, recordingQualityRank, formatRecordingQualityRank }] = loadFrontendModule(['Utilities/Quality/qualityRank.ts']);

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

test('successful file editor response immediately updates the local sort rank without SignalR', () => {
  const requests = [];
  const actions = [];
  const noopFactory = () => () => {};
  const storeAction = (payload) => ({ payload });
  const actionsModule = loadFrontendModule('Store/Actions/bookFileActions.js', {
    'Book/bookEntities': {},
    'Helpers/Props': { sortDirections: { ASCENDING: 'ascending' } },
    'Store/thunks': { createThunk: (name) => name, handleThunks: (handlers) => handlers },
    'Utilities/createAjaxRequest': (request) => {
      requests.push(request);
      return { request: {
        done: (callback) => callback([{ id: 7, qualityCutoffNotMet: false, qualityRank: 0, releaseGroup: 'Owned group' }]),
        fail: () => {}
      } };
    },
    './baseActions': { removeItem: storeAction, set: storeAction, updateItem: storeAction },
    './Creators/createFetchHandler': noopFactory,
    './Creators/createHandleActions': noopFactory,
    './Creators/createRemoveItemHandler': noopFactory,
    './Creators/Reducers/createClearReducer': noopFactory,
    './Creators/Reducers/createSetClientSideCollectionSortReducer': noopFactory,
    './Creators/Reducers/createSetTableOptionReducer': noopFactory,
    'redux-batched-actions': { batchActions: (batch) => batch }
  });

  const quality = { quality: { id: 11 }, revision: { version: 1 } };
  actionsModule.actionHandlers[actionsModule.UPDATE_BOOK_FILES](() => ({}), { bookFileIds: [7], quality }, (action) => actions.push(action));
  assert.equal(requests.length, 1);
  assert.equal(requests[0].method, 'PUT');
  const savedFile = actions[1][0].payload;
  assert.equal(savedFile.id, 7);
  assert.equal(bookFileQualityRank(savedFile), 0);
  assert.equal(savedFile.qualityRank, 0);
});
