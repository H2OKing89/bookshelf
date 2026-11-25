import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { parsePerformanceSnapshot } = loadFrontendModule('System/Status/Performance/performanceProfileQuery.ts');
const group = { operation: 'Book.GetBooks', method: 'GET', requests: 2, failed: 0, cancelled: 1,
  meanMilliseconds: 1.5, maxMilliseconds: 2, sqliteStatements: 8, sqliteMilliseconds: 0.5,
  postgresOperations: 0, postgresMilliseconds: 0 };
const snapshot = { enabled: true, retentionMinutes: 10, capacity: 256, samples: 2, operations: [group] };

test('profiling results expose only the aggregate fields and preserve zero and false', () => {
  assert.deepEqual(parsePerformanceSnapshot({ ...snapshot, rawQuery: 'owned marker', operations: [{ ...group, path: '/owned/private' }] }), snapshot);
  const disabled = { ...snapshot, enabled: false, samples: 0, operations: [] };
  assert.deepEqual(parsePerformanceSnapshot(disabled), disabled);
});

test('profiling parser rejects malformed groups or inconsistent retention', () => {
  for (const changed of [
    { ...snapshot, samples: 257 }, { ...snapshot, samples: 1 }, { ...snapshot, enabled: false },
    { ...snapshot, operations: [{ ...group, operation: '/owned/private' }] },
    { ...snapshot, operations: [{ ...group, method: 'TRACE' }] },
    { ...snapshot, operations: [{ ...group, failed: 3 }] },
    { ...snapshot, operations: [{ ...group, sqliteMilliseconds: Infinity }] },
    { ...snapshot, operations: [{ ...group, postgresOperations: '0' }] }
  ]) assert.throws(() => parsePerformanceSnapshot(changed));
});
