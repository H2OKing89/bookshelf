import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const passthrough = () => null;
const { default: TableSelectCell } = loadFrontendModule('Components/Table/Cells/TableSelectCell.tsx', {
  'Components/Form/CheckInput': passthrough,
  './TableRowCell': passthrough
});
const { default: VirtualTableSelectCell } = loadFrontendModule('Components/Table/Cells/VirtualTableSelectCell.tsx', {
  'Components/Form/CheckInput': passthrough,
  './VirtualTableRowCell': passthrough
});
const { getSelectAllValue } = loadFrontendModule('Components/Table/tableSelection.ts');
const { default: TablePager } = loadFrontendModule('Components/Table/TablePager.tsx', {
  'Components/Form/SelectInput': passthrough,
  'Components/Icon': passthrough,
  'Components/Link/Link': passthrough,
  'Components/Loading/LoadingIndicator': passthrough,
  'Helpers/Props': { icons: {} }
});
const { getDatePresentation } = loadFrontendModule('Components/Table/Cells/RelativeDateCellConnector.tsx', {
  './RelativeDateCell': passthrough
});

test('normal table registration distinguishes false, zero ID, and unmounted row', () => {
  const changes = [];
  const cell = new TableSelectCell({ id: 0, onSelectedChange: (value) => changes.push(value) });
  cell.componentDidMount();
  cell.render().props.children.props.onChange({ name: '0', value: true, shiftKey: true });
  cell.componentWillUnmount();
  assert.deepEqual(changes, [
    { id: 0, value: false },
    { id: 0, value: true, shiftKey: true },
    { id: 0, value: null }
  ]);
});

test('virtual cells emit only explicit user selection for opaque keys', () => {
  const changes = [];
  const cell = new VirtualTableSelectCell({ id: 'queue:ki', onSelectedChange: (value) => changes.push(value) });
  assert.equal(cell.componentWillUnmount, undefined);
  cell.onChange({ name: 'queue:ki', value: false, shiftKey: false });
  assert.deepEqual(changes, [{ id: 'queue:ki', value: false, shiftKey: false }]);
  assert.equal(getSelectAllValue(false, false), null);
  assert.equal(getSelectAllValue(false, true), false);
  assert.equal(getSelectAllValue(true, false), true);
});

test('legacy pager rejects forged or out-of-range page choices and hides an empty page list', () => {
  const selected = [];
  const pager = new TablePager({ page: 1, totalPages: 4, onPageSelect: (value) => selected.push(value) });
  pager.setState = (state) => { pager.state = { ...pager.state, ...state }; };
  for (const value of ['2', 4, '0', '-1', '2x', 1.5, Infinity, '5', '']) {
    pager.onPageSelect({ value });
  }
  assert.deepEqual(selected, [2, 4]);
  assert.equal(new TablePager({ page: 1, totalPages: 0 }).render(), null);
});

test('relative-date projection preserves false and custom formats, defaults only missing values', () => {
  assert.deepEqual(getDatePresentation({ showRelativeDates: false, shortDateFormat: 'YYYY-MM-DD', longDateFormat: 'LL', timeFormat: 'HH:mm' }), {
    showRelativeDates: false, shortDateFormat: 'YYYY-MM-DD', longDateFormat: 'LL', timeFormat: 'HH:mm'
  });
  assert.deepEqual(getDatePresentation({ showRelativeDates: 'false' }), {
    showRelativeDates: true, shortDateFormat: 'MMM D YYYY', longDateFormat: 'dddd, MMMM D YYYY', timeFormat: 'h(:mm)a'
  });
});
