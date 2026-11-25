import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { getDropPosition, getColumnDropIndex, getDelayProfileDropOrder } =
  loadFrontendModule('Utilities/Drag/reorderGeometry');

test('Drag hover positions preserve both halves and exact midpoint', () => {
  const bounds = { top: 10, bottom: 30 };
  assert.equal(getDropPosition(bounds, 19), 'above');
  assert.equal(getDropPosition(bounds, 20), null);
  assert.equal(getDropPosition(bounds, 21), 'below');
});

test('Table column movement keeps zero-based indices and midpoint compatibility', () => {
  assert.equal(getColumnDropIndex(0, 2, 'below'), 2);
  assert.equal(getColumnDropIndex(2, 0, 'above'), 0);
  assert.equal(getColumnDropIndex(2, 0, null), 0);
  assert.equal(getColumnDropIndex(0, 2, 'above'), null);
  assert.equal(getColumnDropIndex(2, 0, 'below'), null);
  assert.equal(getColumnDropIndex(2, 2, 'below'), null);
});

test('Delay profile movement uses one-based insertion order rather than column indices', () => {
  assert.equal(getDelayProfileDropOrder(1, 3, 'below'), 4);
  assert.equal(getDelayProfileDropOrder(3, 1, 'above'), 1);
  assert.equal(getDelayProfileDropOrder(1, 3, 'above'), null);
  assert.equal(getDelayProfileDropOrder(3, 1, null), null);
  assert.equal(getDelayProfileDropOrder(3, 3, 'below'), null);
});

test('Delay profile reorder sends the controller afterId query parameter only when needed', () => {
  const requests = [];
  const updates = [];
  const { default: module, REORDER_DELAY_PROFILE } = loadFrontendModule('Store/Actions/Settings/delayProfiles', {
    'Store/Actions/baseActions': { update: (payload) => payload },
    'Store/Actions/Creators/createFetchHandler': () => () => {},
    'Store/Actions/Creators/createFetchSchemaHandler': () => () => {},
    'Store/Actions/Creators/createRemoveItemHandler': () => () => {},
    'Store/Actions/Creators/createSaveProviderHandler': () => () => {},
    'Store/Actions/Creators/Reducers/createSetSettingValueReducer': () => () => {},
    'Store/thunks': { createThunk: (name) => name },
    'Utilities/createAjaxRequest': (request) => {
      requests.push(request);
      return { request: { done: (callback) => callback([{ id: 2, order: 2 }]) } };
    }
  });
  const getState = () => ({ settings: { delayProfiles: { items: [
    { id: 2, order: 1 }, { id: 3, order: 2 }, { id: 1, order: 2147483647 }
  ] } } });
  const reorder = (payload) => module.actionHandlers[REORDER_DELAY_PROFILE](getState, payload, (action) => updates.push(action));

  reorder({ id: 2, moveIndex: 2 });
  assert.deepEqual(requests[0], { method: 'PUT', url: '/delayprofile/reorder/2?afterId=3' });
  assert.deepEqual(updates[0], { section: 'settings.delayProfiles', data: [{ id: 2, order: 2 }] });

  // The first position omits the optional parameter, and same-order moves are ignored.
  reorder({ id: 3, moveIndex: 0 });
  assert.equal(requests[1].url, '/delayprofile/reorder/3?');
  reorder({ id: 2, moveIndex: 0 });
  reorder({ id: 99, moveIndex: 0 });
  reorder({ id: 2, moveIndex: 99 });
  assert.equal(requests.length, 2);
});


test('Quality profile preview and drop calculate the same group insertion without stale render state', () => {
  const { adjustQualityDropMove, parseIndex } =
    loadFrontendModule('Settings/Profiles/Quality/qualityProfileDragTypes');
  assert.deepEqual(parseIndex('2.3'), [1, 2]);
  assert.deepEqual(parseIndex('1'), [null, 0]);
  const move = (dragQualityIndex, dropQualityIndex, dropPosition) =>
    adjustQualityDropMove({ dragQualityIndex, dropQualityIndex, dropPosition });
  assert.deepEqual(move('3', '2', 'below'), {
    dragQualityIndex: '3', dropQualityIndex: '2', dropPosition: 'below'
  });
  assert.equal(move('2', '3', 'below'), null); // Already directly below.
  assert.deepEqual(move('1', '3.1', 'above'), {
    dragQualityIndex: '1', dropQualityIndex: '3.2', dropPosition: 'above'
  });
});
