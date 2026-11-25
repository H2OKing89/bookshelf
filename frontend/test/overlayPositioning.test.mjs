import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { createTitleRegistry } = loadFrontendModule('Components/Page/DocumentTitle');
const { selectedOptionIndex, nextEnabledOptionIndex } = loadFrontendModule('Components/Form/selectInputSelection');

test('document title prefers the deepest mounted scope despite child-first effects', () => {
  let active;
  const registry = createTitleRegistry('Bookshelf', title => { active = title; });
  const parent = Symbol();
  const child = Symbol();

  const removeChild = registry.register(child, { title: 'Recordings - Bookshelf', depth: 2 });
  const removeParent = registry.register(parent, { title: 'Settings - Bookshelf', depth: 1 });
  assert.equal(active, 'Recordings - Bookshelf');
  registry.setFallback('Owned');
  assert.equal(active, 'Recordings - Bookshelf');
  removeChild();
  assert.equal(active, 'Settings - Bookshelf');
  removeParent();
  assert.equal(active, 'Owned');
});

test('a route title change and unmount restore the instance fallback', () => {
  let active;
  const registry = createTitleRegistry('Bookshelf', title => { active = title; });
  const key = Symbol();

  const first = registry.register(key, { title: 'Books', depth: 1 });
  assert.equal(active, 'Books');
  first();
  assert.equal(active, 'Bookshelf');
  const second = registry.register(key, { title: 'Authors', depth: 1 });
  assert.equal(active, 'Authors');
  second();
  assert.equal(active, 'Bookshelf');
});

test('select navigation retains zero and typed identities and stops on disabled/empty lists', () => {
  const options = [{ key: '0', isDisabled: true }, { key: 0 }, { key: 2, isDisabled: true }, { key: 3 }];
  assert.equal(selectedOptionIndex([0, 3], options), 1);
  assert.equal(selectedOptionIndex('0', options), 0);
  assert.equal(selectedOptionIndex([], options), -1);
  assert.equal(nextEnabledOptionIndex(-1, -1, options), 3);
  assert.equal(nextEnabledOptionIndex(-1, 1, options), 1);
  assert.equal(nextEnabledOptionIndex(1, 1, options), 3);
  assert.equal(nextEnabledOptionIndex(3, 1, options), 1);
  assert.equal(nextEnabledOptionIndex(2, -1, options), 1);
  assert.equal(nextEnabledOptionIndex(-1, 1, []), -1);
  assert.equal(nextEnabledOptionIndex(-1, -1, [{ key: 0, isDisabled: true }]), -1);
});
