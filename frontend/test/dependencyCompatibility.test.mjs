import assert from 'node:assert/strict';
import { test } from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

test('binary size formatting preserves audiobook sizes and rejects invalid non-finite values', () => {
  const { default: formatBytes } = loadFrontendModule('Utilities/Number/formatBytes.ts');
  assert.equal(formatBytes(0), '0 B');
  assert.equal(formatBytes(1536), '1.5 KiB');
  assert.equal(formatBytes(123456789), '117.7 MiB');
  assert.equal(formatBytes(1073741824), '1 GiB');
  assert.equal(formatBytes('1024', true), '8 Kibit');
  assert.equal(formatBytes(-1024), '-1 KiB');
  assert.equal(formatBytes(undefined), '');
  assert.equal(formatBytes('invalid'), '');
  assert.equal(formatBytes(Infinity), '');
  assert.equal(formatBytes(-Infinity), '');
});

test('global search worker preserves name ranking, nested tags, match indices and result limit', () => {
  const previousOnMessage = Object.getOwnPropertyDescriptor(globalThis, 'onmessage');
  const previousSelf = Object.getOwnPropertyDescriptor(globalThis, 'self');
  let message;
  globalThis.onmessage = undefined;
  globalThis.self = { postMessage: (value) => { message = value; } };

  try {
    loadFrontendModule('Components/Page/Header/fuse.worker.js');
    const items = [
      { id: 1, name: 'Sword Art Online', title: 'Sword Art Online', firstCharacter: 's', tags: [{ label: 'LitRPG' }] },
      { id: 2, name: 'The Unital Ring', title: 'The Unital Ring', firstCharacter: 't', tags: [{ label: 'Adventure' }] },
      { id: 3, name: 'Starfall', title: 'Starfall', firstCharacter: 's', tags: [{ label: 'Science Fiction' }] }
    ];

    const search = (value, source = items) => {
      globalThis.onmessage({ data: { value, items: source } });
      assert.equal(message.value, value);
      return message.suggestions;
    };

    const name = search('sword art onlin');
    assert.equal(name[0].item.id, 1);
    assert.ok(name[0].matches.some((match) => match.key === 'name' && match.indices.length > 0));
    const tag = search('litrpg');
    assert.equal(tag[0].item.id, 1);
    assert.ok(tag[0].matches.some((match) => match.key === 'tags.label' && match.value === 'LitRPG'));
    assert.deepEqual(search('S').map((entry) => entry.item.id), [1, 3]);
    const many = Array.from({ length: 14 }, (_, id) => ({ id, name: `Owned recording ${id}` }));
    assert.equal(search('owned', many).length, 10);
  } finally {
    if (previousOnMessage) {
      Object.defineProperty(globalThis, 'onmessage', previousOnMessage);
    } else {
      delete globalThis.onmessage;
    }
    if (previousSelf) {
      Object.defineProperty(globalThis, 'self', previousSelf);
    } else {
      delete globalThis.self;
    }
  }
});
