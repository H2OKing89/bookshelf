import assert from 'node:assert/strict';
import test from 'node:test';
import moment from 'moment';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const load = (name, replacements) => loadFrontendModule('Utilities/' + name, replacements).default;
const getSelectedIds = load('Table/getSelectedIds.ts');
const toggleSelected = load('Table/toggleSelected.ts');
const getToggledRange = load('Table/getToggledRange.ts');
const selectAll = load('Table/selectAll.ts');
const areAllSelected = load('Table/areAllSelected.ts');
const removeOldSelectedState = load('Table/removeOldSelectedState.ts');

test('selection decodes exact numeric IDs and retains opaque string IDs when requested', () => {
  const state = { '0': true, '7': true, '12': false, '17garbage': true, '9007199254740993': true, opaque: true };
  assert.deepEqual(getSelectedIds(state), [0, 7]);
  assert.deepEqual(getSelectedIds(state, { parseIds: false }), ['0', '7', '17garbage', '9007199254740993', 'opaque']);
  assert.deepEqual(areAllSelected({}), { allSelected: true, allUnselected: true });
  assert.deepEqual(selectAll({ '1': false, '2': true }, true), {
    allSelected: true, allUnselected: false, lastToggled: null, selectedState: { '1': true, '2': true }
  });
});

test('range toggle preserves forward/backward selection and does not use filtered-out rows', () => {
  const items = [{ id: 1 }, { id: 2 }, { id: 3 }];
  const state = { selectedState: { '1': false, '2': false, '3': false }, lastToggled: 1 };
  assert.deepEqual(toggleSelected(state, items, 3, true, true).selectedState, { '1': true, '2': true, '3': true });
  assert.deepEqual(toggleSelected({ ...state, lastToggled: 3 }, items, 1, true, true).selectedState, { '1': true, '2': true, '3': true });
  assert.equal(getToggledRange(items, 3, 90), null);
  assert.deepEqual(toggleSelected({ ...state, lastToggled: 90 }, items, 3, true, true).selectedState, { '1': false, '2': false, '3': true });
  assert.deepEqual(toggleSelected(state, items, 2, undefined, true).selectedState, { '1': false, '3': false });
  assert.deepEqual(removeOldSelectedState({ selectedState: { '1': true, '2': false } }, [{ id: 2 }]), { allSelected: true, allUnselected: false, selectedState: { '1': true } });
  assert.equal(state.selectedState['3'], false, 'prior selection is not mutated');
});

test('collection helpers preserve case-sensitive identities, caller chosen keys, and order', () => {
  const removed = load('Object/getRemovedItems.ts');
  const changed = load('Object/hasDifferentItems.ts');
  const reordered = load('Object/hasDifferentItemsOrOrder.ts');
  const unique = load('Object/selectUniqueIds.ts');
  const us = { recording: 'audible:us:B011111111' };
  const uk = { recording: 'audible:uk:B011111111' };
  const both = [us, uk];
  assert.deepEqual(removed(both, [uk], 'recording'), [us]);
  assert.equal(changed(both, [uk, us], 'recording'), false);
  assert.equal(reordered(both, [uk, us], 'recording'), true);
  assert.deepEqual(unique([us, us, uk, { recording: '' }], 'recording'), [us.recording, uk.recording]);
  assert.deepEqual(removed(both, both, 'recording'), []);
});

test('date formatting keeps relative, calendar-day, zero-minute and duration behavior', () => {
  const formatDate = load('Date/formatDate.ts');
  const formatTime = load('Date/formatTime.ts');
  const duration = load('Date/formatTimeSpan.ts');
  const getRelativeDate = load('Date/getRelativeDate.ts');
  const isAfter = load('Date/isAfter.ts');
  const isBefore = load('Date/isBefore.ts');
  const realNow = moment.now;
  moment.now = () => new Date(2026, 10, 25, 12, 0).valueOf();
  try {
    assert.equal(formatDate(null, 'YYYY-MM-DD'), '');
    assert.equal(formatDate('2026-11-25', 'YYYY-MM-DD'), '2026-11-25');
    assert.equal(formatTime(new Date(2026, 10, 25, 12, 0), 'H(:mm)'), '12');
    assert.equal(formatTime(new Date(2026, 10, 25, 12, 0), 'H(:mm)', { includeMinuteZero: true }), '12:00');
    assert.equal(getRelativeDate(new Date(2026, 10, 24, 12), 'YYYY-MM-DD', true), 'Yesterday');
    assert.equal(getRelativeDate(undefined, 'YYYY-MM-DD', true), null);
    assert.equal(duration('1.02:03:04'), '1d 02:03:04');
    assert.equal(isBefore(new Date(2026, 10, 24, 12), { days: -1 }), false);
    assert.equal(isAfter(new Date(2026, 10, 24, 13), { days: -1 }), true);
    assert.equal(isAfter(undefined), false);
  } finally {
    moment.now = realNow;
  }
});

test('relative date filters validate persisted quantities without inventing date matches', () => {
  const filterTypes = loadFrontendModule('Helpers/Props/filterTypes');
  const predicate = load('Date/dateFilterPredicate.ts', { 'Helpers/Props/filterTypes': filterTypes });
  const now = moment.now;
  moment.now = () => new Date(2026, 10, 25, 12).valueOf();
  try {
    const recent = new Date(2026, 10, 24, 13);
    assert.equal(predicate(recent, { time: 'days', value: '1' }, filterTypes.IN_LAST), true);
    assert.equal(predicate(recent, { time: 'days', value: 0 }, filterTypes.IN_LAST), false);
    assert.equal(predicate(recent, { time: 'not-a-unit', value: 1 }, filterTypes.IN_LAST), false);
    assert.equal(predicate(recent, { time: 'days', value: 'invalid' }, filterTypes.NOT_IN_LAST), false);
    assert.equal(predicate(recent, '2026-11-25', filterTypes.LESS_THAN), true);
  } finally {
    moment.now = now;
  }
});

test('number and string formatting preserve units, falsey inputs and local UUID versions', () => {
  const convert = load('Number/convertToBytes.ts');
  const pad = load('Number/padNumber.ts');
  const formatBytes = load('Number/formatBytes.ts');
  const uuid = load('String/generateUUIDv4.ts');
  assert.equal(convert('1.5', 1, true), 1536);
  assert.equal(convert('not-a-number', 1, false), '');
  assert.equal(pad(0, 3), '000');
  assert.equal(pad(null, 2), '');
  assert.equal(formatBytes(0), '0 B');
  assert.equal(load('String/combinePath.ts')(true, 'C:\\Library', ['Author', 'Book']), 'C:\\Library\\Author\\Book');
  assert.deepEqual(load('String/split.ts')('a,,b'), ['a', 'b']);
  assert.equal(load('String/naturalExpansion.ts')('Vol 9'), 'Vol 00000009');
  assert.match(uuid(), /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  const values = new Set(Array.from({ length: 50 }, uuid));
  assert.equal(values.size, 50);
});

test('error envelopes fall back when an invalid server message cannot be displayed', () => {
  const message = load('Object/getErrorMessage.ts');
  assert.equal(message({ responseJSON: { message: 'Owned message' } }, 'Fallback'), 'Owned message');
  assert.equal(message({ responseJSON: { message: { secret: 'opaque' } } }, 'Fallback'), 'Fallback');
  assert.equal(message(null, 'Fallback'), 'Fallback');
});
