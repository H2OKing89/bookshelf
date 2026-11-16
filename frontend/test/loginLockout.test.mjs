import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const html = readFileSync(new URL('../src/login.html', import.meta.url), 'utf8');
const script = html.match(/<script type="text\/javascript">([\s\S]*?)<\/script>/)[1];
const start = Date.parse('2026-11-15T12:00:00Z');

function load(search = '') {
  let now = start;
  let nextTimer = 0;
  const timers = new Map();
  const events = new Map();
  const elements = new Map();
  for (const id of ['year', 'copy', 'login-failed', 'login-lockout', 'login-button']) {
    const classes = new Set(id === 'year' || id === 'login-button' ? [] : ['hidden']);
    elements.set(id, {
      disabled: false,
      textContent: '',
      set innerHTML(value) { throw new Error(`Unexpected HTML assignment: ${value}`); },
      classList: { add: (name) => classes.add(name), remove: (name) => classes.delete(name) },
      hidden: () => classes.has('hidden')
    });
  }
  class Clock extends Date {
    constructor(...args) { super(...(args.length ? args : [now])); }
    static now() { return now; }
  }
  const window = {
    location: { search },
    setTimeout(callback, delay) {
      const id = ++nextTimer;
      timers.set(id, { callback, delay, due: now + delay });
      return id;
    },
    clearTimeout: (id) => timers.delete(id),
    addEventListener: (name, callback) => events.set(name, callback)
  };
  vm.runInNewContext(script, { Date: Clock, URLSearchParams, Number, Math, window,
    document: { getElementById: (id) => elements.get(id) } });
  return {
    element: (id) => elements.get(id), timers,
    hide: () => events.get('pagehide')?.(),
    show: () => events.get('pageshow')?.(),
    advance(milliseconds) {
      now += milliseconds;
      for (const [id, timer] of [...timers]) {
        if (timer.due <= now) {
          timers.delete(id);
          timer.callback();
        }
      }
    }
  };
}

test('normal login remains usable and shows the copyright safely', () => {
  const page = load();
  assert.equal(page.element('login-button').disabled, false);
  assert.equal(page.element('login-failed').hidden(), true);
  assert.equal(page.element('login-lockout').hidden(), true);
  assert.equal(page.element('year').textContent, '2017-2026');
  assert.equal(page.element('copy').hidden(), false);
  assert.equal(page.timers.size, 0);
});

test('only the actual loginFailed query parameter shows a password error', () => {
  assert.equal(load('?loginFailed=true').element('login-failed').hidden(), false);
  for (const query of ['?returnUrl=%2Fbooks%3FloginFailed%3Dtrue', '?loginFailed=false', '?loginFailed=trueish']) {
    assert.equal(load(query).element('login-failed').hidden(), true);
  }
});

test('lockout gives its own status and releases the submit button at expiry', () => {
  const end = new Date(start + 300_000).toISOString();
  const page = load(`?loginFailed=true&lockedUntil=${encodeURIComponent(end)}`);
  assert.equal(page.element('login-button').disabled, true);
  assert.equal(page.element('login-failed').hidden(), true);
  assert.equal(page.element('login-lockout').hidden(), false);
  assert.match(page.element('login-lockout').textContent, /Too many login attempts/);
  page.advance(299_999);
  assert.equal(page.element('login-button').disabled, true);
  page.advance(1);
  assert.equal(page.element('login-button').disabled, false);
  assert.equal(page.element('login-lockout').textContent, 'You can try signing in again.');
  assert.equal(page.timers.size, 0);
});

test('expired or invalid lockout parameters cannot disable login', () => {
  for (const value of ['', 'invalid', '<img src=x onerror=alert(1)>', new Date(start - 1).toISOString()]) {
    const page = load(`?lockedUntil=${encodeURIComponent(value)}`);
    assert.equal(page.element('login-button').disabled, false);
    assert.equal(page.element('login-lockout').hidden(), true);
    assert.equal(page.timers.size, 0);
  }
});

test('leaving the page clears its release timer', () => {
  const page = load(`?lockedUntil=${encodeURIComponent(new Date(start + 300_000).toISOString())}`);
  assert.equal(page.timers.size, 1);
  page.hide();
  assert.equal(page.timers.size, 0);
});

test('far-future timestamps cannot overflow the browser timer', () => {
  const page = load('?lockedUntil=9999-01-01T00%3A00%3A00Z');
  assert.equal(page.element('login-button').disabled, true);
  assert.equal([...page.timers.values()][0].delay, 2147483647);
  page.advance(2147483647);
  assert.equal(page.element('login-button').disabled, true);
  assert.equal(page.timers.size, 1);
});

test('restoring from browser history resumes the timer and releases an expired lockout', () => {
  const page = load(`?lockedUntil=${encodeURIComponent(new Date(start + 300_000).toISOString())}`);
  page.hide();
  page.advance(60_000);
  page.show();
  assert.equal(page.element('login-button').disabled, true);
  assert.equal(page.timers.size, 1);
  page.show();
  assert.equal(page.timers.size, 1);
  page.hide();
  page.advance(240_000);
  page.show();
  assert.equal(page.element('login-button').disabled, false);
  assert.equal(page.timers.size, 0);
});
