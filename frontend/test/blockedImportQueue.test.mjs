import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function Icon() {}
function Popover() {}
const { default: QueueStatusCell } = loadFrontendModule('Activity/Queue/QueueStatusCell.js', {
  'Components/Icon': Icon,
  'Components/Table/Cells/TableRowCell': () => null,
  'Components/Tooltip/Popover': Popover,
  'Helpers/Props': { icons: { DOWNLOADED: 'downloaded', DOWNLOADING: 'downloading' }, kinds: { DEFAULT: 'default', WARNING: 'warning', DANGER: 'danger' }, tooltipPositions: { RIGHT: 'right' } },
  'Utilities/String/translate': (key) => key
});

const props = {
  sourceTitle: 'Owned recording', status: 'completed', trackedDownloadState: 'importBlocked',
  trackedDownloadStatus: 'warning', statusMessages: [{ title: 'Part 1', messages: ['Correct the local path'] }]
};

test('a recoverable import block is named and keeps its readable diagnostics', () => {
  const popover = QueueStatusCell(props).props.children;
  assert.equal(popover.type, Popover);
  assert.equal(popover.props.title, 'Downloaded - Import blocked');
  assert.equal(popover.props.anchor.props.kind, 'warning');
  const messageGroup = popover.props.body.props.children[0];
  assert.equal(messageGroup.props.children[0], 'Part 1');
  assert.equal(messageGroup.props.children[1].props.children[0].props.children, 'Correct the local path');
});

test('a real client failure still takes priority over the import-block title', () => {
  const popover = QueueStatusCell({ ...props, status: 'failed' }).props.children;
  assert.equal(popover.props.title, 'Download failed');
  assert.equal(popover.props.anchor.props.kind, 'danger');
});
