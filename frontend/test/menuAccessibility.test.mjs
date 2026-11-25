import assert from 'node:assert/strict';
import test from 'node:test';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const h = React.createElement;
const passthrough = ({ children }) => children;
const [Menu, MenuContent, SelectedMenuItem, MenuItem] = loadFrontendModule([
  'Menu', 'MenuContent', 'SelectedMenuItem', 'MenuItem'
].map(name => 'Components/Menu/' + name), {
  'Components/Portal': passthrough,
  'Components/Scroller/Scroller': passthrough,
  'Components/Icon': () => null,
  'Components/Link/Link': ({ to, isDisabled, onPress, ...props }) =>
    h(to ? 'a' : 'button', { ...props, href: to, disabled: isDisabled || undefined, onClick: onPress }),
  'Helpers/Props': { align: { LEFT: 'left', RIGHT: 'right' }, icons: { CHECK: {} } },
  'Components/Positioning/AnchoredPopup': {
    default: ({ children }) => children({ ref: () => {}, style: {} }),
    __esModule: true
  }
}).map(module => module.default);

function makeMenu(props) {
  return new Menu({
    ...Menu.defaultProps,
    children: [
      h('button', { key: 'trigger' }, 'Sort'),
      h(MenuContent, { key: 'content' }, 'Contents')
    ],
    ...props
  });
}

function key(name) {
  return {
    key: name,
    prevented: false,
    stopped: false,
    preventDefault() { this.prevented = true; },
    stopPropagation() { this.stopped = true; }
  };
}

test('menu triggers label only their own popup and expose the open state', () => {
  const first = makeMenu();
  const second = makeMenu();
  const markup = renderToStaticMarkup(first.render());
  assert.match(markup, /aria-haspopup="menu" aria-expanded="false"/);
  assert.doesNotMatch(markup, /role="menu"/);
  first.setReference({ closest: () => null });
  first.state = { isOpen: true, maxWidth: 284, maxHeight: 354 };

  const opened = renderToStaticMarkup(first.render());
  const contentId = /aria-controls="([^"]+)"/.exec(opened)[1];
  const triggerId = /<button id="([^"]+)"/.exec(opened)[1];
  assert.match(opened, /aria-expanded="true"/);
  assert.ok(opened.includes(`role="menu" tabindex="-1" id="${contentId}" aria-labelledby="${triggerId}"`));
  assert.match(opened, /max-width:284px;max-height:354px/);
  assert.ok(!renderToStaticMarkup(second.render()).includes(`id="${triggerId}"`));
});

test('keyboard focus wraps over actionable choices and Escape or Tab restores the trigger', (t) => {
  const listeners = new Map();
  let nextFrame;
  globalThis.document = {
    activeElement: null,
    documentElement: { clientWidth: 300, clientHeight: 500 },
    addEventListener: (type, listener) => listeners.set('document:' + type, listener),
    removeEventListener: type => listeners.delete('document:' + type)
  };
  globalThis.window = {
    getComputedStyle: item => ({ visibility: item.visibility }),
    addEventListener: (type, listener) => listeners.set('window:' + type, listener),
    removeEventListener: type => listeners.delete('window:' + type),
    requestAnimationFrame: callback => { nextFrame = callback; return 1; },
    cancelAnimationFrame: () => { nextFrame = undefined; }
  };
  t.after(() => {
    delete globalThis.document;
    delete globalThis.window;
  });

  function item({ disabled = false, hidden = false, visibility = 'visible' } = {}) {
    return {
      visibility,
      matches: () => disabled,
      closest: () => hidden,
      getClientRects: () => [{}],
      getBoundingClientRect: () => ({ top: 100, bottom: 140 }),
      focus() { document.activeElement = this; }
    };
  }

  const trigger = item();
  const first = item();
  const last = item();
  const choices = [first, item({ disabled: true }), item({ hidden: true }), item({ visibility: 'hidden' }), last];
  const menu = makeMenu();
  menu.setState = state => {
    const previous = menu.state;
    menu.state = { ...previous, ...state };
    menu.componentDidUpdate(menu.props, previous);
  };
  menu.setReference({
    querySelector: () => trigger,
    contains: node => node === trigger,
    getBoundingClientRect: () => ({ top: 420, bottom: 450 })
  });
  menu.onFloatingChange({
    contains: node => choices.includes(node),
    querySelectorAll: () => choices,
    firstElementChild: { getBoundingClientRect: () => ({ top: 20, bottom: 400 }) }
  });
  trigger.focus();

  const open = key('ArrowUp');
  menu.onTriggerKeyDown(open);
  assert.equal(open.prevented, true);
  assert.equal(menu.state.maxHeight, 412);
  menu.onPositioned();
  nextFrame();
  assert.equal(document.activeElement, last);
  menu.onMenuKeyDown(key('ArrowDown'));
  assert.equal(document.activeElement, first);
  menu.onMenuKeyDown(key('ArrowUp'));
  assert.equal(document.activeElement, last);
  menu.onMenuKeyDown(key('Home'));
  assert.equal(document.activeElement, first);
  menu.onMenuKeyDown(key('End'));
  assert.equal(document.activeElement, last);

  const escape = key('Escape');
  menu.onMenuKeyDown(escape);
  assert.equal(escape.prevented, true);
  assert.equal(escape.stopped, true);
  assert.equal(document.activeElement, trigger);
  assert.equal(listeners.size, 0);

  menu.onTriggerPress();
  menu.onPositioned();
  nextFrame();
  const tab = key('Tab');
  menu.onMenuKeyDown(tab);
  assert.equal(tab.prevented, false);
  assert.equal(document.activeElement, trigger);
  assert.equal(listeners.size, 0);

  menu.onTriggerPress();
  menu.onPositioned();
  nextFrame();
  listeners.get('document:pointerdown')({ target: {} });
  assert.equal(menu.state.isOpen, false);
  assert.equal(document.activeElement, first);
  assert.equal(listeners.size, 0);
});

test('selected options and disabled destinations carry menu semantics', () => {
  const markup = renderToStaticMarkup(h(React.Fragment, null,
    h(SelectedMenuItem, { isSelected: true, onPress: () => {} }, 'Title'),
    h(MenuItem, { isDisabled: true, to: '/disabled' }, 'Unsupported')));
  assert.match(markup, /role="menuitemradio" aria-checked="true" tabindex="-1"/);
  assert.match(markup, /role="menuitem" tabindex="-1" aria-disabled="true" disabled=""/);
  assert.doesNotMatch(markup, /href=/);
});
