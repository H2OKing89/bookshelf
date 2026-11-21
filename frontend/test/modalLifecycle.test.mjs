import assert from 'node:assert/strict';
import test from 'node:test';
import { createContext, createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server.js';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function setup(t) {
  const listeners = new Map();
  const body = { style: { top: '3px' } };
  const scrollLocks = [];
  const scrolls = [];
  const element = (parent) => ({
    parent,
    isConnected: true,
    focus() { document.activeElement = this; }
  });
  const launcher = element();
  const titleContext = { ModalTitleContext: createContext(undefined) };

  globalThis.document = { activeElement: launcher, body, getElementById: () => ({}) };
  globalThis.window = {
    scrollX: 4,
    scrollY: 280,
    scrollTo: (...position) => scrolls.push(position),
    addEventListener: (type, handler) => listeners.set(type, handler),
    removeEventListener: (type, handler) => {
      assert.equal(listeners.get(type), handler);
      listeners.delete(type);
    }
  };
  t.after(() => {
    delete globalThis.document;
    delete globalThis.window;
  });

  const { default: Modal } = loadFrontendModule('Components/Modal/Modal.js', {
    'element-class': () => ({ add() {}, remove() {} }),
    'react-dom': { createPortal: (contents) => contents },
    'react-focus-lock': ({ children }) => children,
    'Components/Error/ErrorBoundary': ({ children }) => children,
    'Helpers/Props': { sizes: { all: [] } },
    'Utilities/browser': { isIOS: () => true },
    'Utilities/scrollLock': { setScrollLock: (isLocked) => scrollLocks.push(isLocked) },
    './ModalContext': titleContext,
    './ModalError': () => null
  });
  const { default: ModalHeader } = loadFrontendModule('Components/Modal/ModalHeader.js', {
    './ModalContext': titleContext
  });

  function open(title) {
    const props = {
      isOpen: true,
      closeOnBackgroundClick: true,
      onModalClose: () => modal.componentWillUnmount(),
      children: createElement(ModalHeader, null, title)
    };
    const modal = new Modal(props);
    modal.setState = (state) => { modal.state = { ...modal.state, ...state }; };
    modal.dialogRef.current = { contains: (target) => target.parent === modal };
    modal.componentDidMount();
    return modal;
  }

  function escape({ repeat = false } = {}) {
    listeners.get('keydown')?.({ key: 'Escape', repeat, preventDefault() {}, stopPropagation() {} });
  }

  return { listeners, launcher, body, scrollLocks, scrolls, element, open, escape };
}

test('labels each dialog and closes only the active layer on Escape', async (t) => {
  const { listeners, launcher, body, scrolls, element, open, escape } = setup(t);
  const outer = open('Manage Library');
  const deleteButton = element(outer);
  deleteButton.focus();
  const inner = open('Delete Selected Files');
  const cancelButton = element(inner);
  cancelButton.focus();

  const innerMarkup = renderToStaticMarkup(inner.render());
  const outerMarkup = renderToStaticMarkup(outer.render());
  const innerTitle = /aria-labelledby="([^"]+)"/.exec(innerMarkup)[1];
  const outerTitle = /aria-labelledby="([^"]+)"/.exec(outerMarkup)[1];
  assert.notEqual(innerTitle, outerTitle);
  assert.match(innerMarkup, new RegExp(`<h2 id="${innerTitle}">Delete Selected Files</h2>`));
  assert.match(outerMarkup, new RegExp(`<h2 id="${outerTitle}">Manage Library</h2>`));
  assert.match(innerMarkup, /role="dialog" aria-modal="true"/);
  assert.match(outerMarkup, /role="dialog" aria-hidden="true"/);

  escape();
  await Promise.resolve();
  assert.equal(document.activeElement, deleteButton);
  assert.equal(outer.state.isTopModal, true);
  assert.equal(body.style.top, '-280px');
  assert.equal(listeners.size, 1);

  escape({ repeat: true });
  assert.equal(listeners.size, 1);
  escape();
  await Promise.resolve();
  assert.equal(document.activeElement, launcher);
  assert.equal(listeners.size, 0);
  assert.deepEqual(scrolls, [[4, 280]]);
});

test('retains the original page scroll and opener if the lower dialog closes first', async (t) => {
  const { launcher, body, scrollLocks, scrolls, element, open } = setup(t);
  const outer = open('Editor');
  const button = element(outer);
  button.focus();
  const inner = open('Confirm');

  outer.componentWillUnmount();
  button.isConnected = false;
  assert.equal(body.style.top, '-280px');
  assert.deepEqual(scrolls, []);
  assert.deepEqual(scrollLocks, [true]);

  inner.componentWillUnmount();
  await Promise.resolve();
  assert.equal(body.style.top, '3px');
  assert.deepEqual(scrolls, [[4, 280]]);
  assert.deepEqual(scrollLocks, [true, false]);
  assert.equal(document.activeElement, launcher);
});

test('backdrop dismissal requires a stationary primary mouse press on the active backdrop', async (t) => {
  const { listeners, open } = setup(t);
  const modal = open('Editor');
  const backdrop = {};
  const content = {};
  modal._setBackgroundRef(backdrop);
  const press = { target: backdrop, button: 0, clientX: 25, clientY: 30 };

  modal.onBackdropBeginPress({ ...press, target: content });
  modal.onBackdropEndPress(press);
  modal.onBackdropBeginPress(press);
  modal.onBackdropEndPress({ ...press, clientX: 50 });
  modal.onBackdropBeginPress({ ...press, button: 2 });
  modal.onBackdropEndPress(press);
  assert.equal(listeners.size, 1);

  modal.onBackdropBeginPress(press);
  modal.onBackdropEndPress(press);
  assert.equal(listeners.size, 0);
  await Promise.resolve();
});

test('destructive confirmations autofocus Cancel and require the actual Delete button for confirmation', () => {
  let cancels = 0;
  let deletes = 0;
  const passthrough = ({ children }) => children;
  const button = ({ children, onPress, autoFocus }) =>
    createElement('button', { autoFocus, onClick: onPress }, children);
  const { default: ConfirmModal } = loadFrontendModule('Components/Modal/ConfirmModal.js', {
    'Components/Link/Button': button,
    'Components/Link/SpinnerButton': button,
    'Components/Modal/Modal': passthrough,
    'Components/Modal/ModalContent': passthrough,
    'Components/Modal/ModalHeader': passthrough,
    'Components/Modal/ModalBody': passthrough,
    'Components/Modal/ModalFooter': passthrough,
    'Helpers/Props': { kinds: { DANGER: 'danger', all: ['danger'] }, sizes: { all: [] } }
  });
  const element = createElement(ConfirmModal, {
    isOpen: true,
    kind: 'danger',
    title: 'Delete Files',
    message: 'Delete the selected files?',
    confirmLabel: 'Delete',
    onCancel: () => { cancels++; },
    onConfirm: () => { deletes++; }
  });

  const markup = renderToStaticMarkup(element);
  assert.match(markup, /<button autofocus="">Cancel<\/button>/);
  assert.match(markup, /<button>Delete<\/button>/);
  const footer = ConfirmModal(element.props).props.children.props.children[2];
  const [cancel, confirm] = footer.props.children;
  cancel.props.onPress();
  assert.equal(cancels, 1);
  assert.equal(deletes, 0);
  confirm.props.onPress();
  assert.equal(deletes, 1);
});
