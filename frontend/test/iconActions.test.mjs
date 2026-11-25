import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const Icon = () => null;
const Link = () => null;
const spinner = { iconName: 'spinner' };
const icon = { iconName: 'download' };
const boundaries = {
  'Components/Icon': Icon,
  'Helpers/Props': { icons: { SPINNER: spinner } },
  './Link': Link
};

const { default: IconButton } = loadFrontendModule('Components/Link/IconButton.tsx', boundaries);
const { default: SpinnerIconButton } = loadFrontendModule('Components/Link/SpinnerIconButton.tsx', {
  ...boundaries,
  './IconButton': IconButton
});

test('icon actions use the caller name and hide the decorative glyph', () => {
  const titled = IconButton({ name: icon, title: 'Refresh Book' });
  assert.equal(titled.type, Link);
  assert.equal(titled.props['aria-label'], 'Refresh Book');

  const named = IconButton({
    name: icon,
    title: 'Connection to the download client failed',
    'aria-label': 'Grab Release'
  });
  assert.equal(named.props['aria-label'], 'Grab Release');
  assert.equal(named.props.title, 'Connection to the download client failed');
  assert.equal(named.props.children.type, Icon);
  assert.equal(named.props.children.props['aria-hidden'], true);
});

test('a busy icon action keeps its name and disables activation', () => {
  const button = SpinnerIconButton({
    className: 'grab-action',
    name: icon,
    spinningName: spinner,
    isSpinning: true,
    'aria-label': 'Grab Release'
  });
  assert.equal(button.type, IconButton);
  assert.equal(button.props.name, spinner);

  const link = IconButton(button.props);
  assert.equal(link.props['aria-label'], 'Grab Release');
  assert.equal(link.props.isDisabled, true);
});
