import { queryOptions } from '@tanstack/react-query';
import fetchJson from 'Utilities/Fetch/fetchJson';

export interface PerformanceOperation {
  operation: string;
  method: string;
  requests: number;
  failed: number;
  cancelled: number;
  meanMilliseconds: number;
  maxMilliseconds: number;
  sqliteStatements: number;
  sqliteMilliseconds: number;
  postgresOperations: number;
  postgresMilliseconds: number;
}

export interface PerformanceSnapshot {
  enabled: boolean;
  retentionMinutes: number;
  capacity: number;
  samples: number;
  operations: PerformanceOperation[];
}

function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('Invalid performance response');
  }
  return value as Record<string, unknown>;
}

function count(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new Error('Invalid performance count');
  }
  return value;
}

function duration(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) {
    throw new Error('Invalid performance duration');
  }
  return value;
}

function parseOperation(value: unknown): PerformanceOperation {
  const item = record(value);
  if (typeof item.operation !== 'string' || !/^[A-Za-z][A-Za-z0-9_.]*$/.test(item.operation) ||
      typeof item.method !== 'string' || !['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS', 'OTHER'].includes(item.method)) {
    throw new Error('Invalid performance operation');
  }
  const requests = count(item.requests);
  const failed = count(item.failed);
  const cancelled = count(item.cancelled);
  if (requests === 0 || failed > requests || cancelled > requests) {
    throw new Error('Invalid operation counts');
  }
  return {
    operation: item.operation,
    method: item.method,
    requests,
    failed,
    cancelled,
    meanMilliseconds: duration(item.meanMilliseconds),
    maxMilliseconds: duration(item.maxMilliseconds),
    sqliteStatements: count(item.sqliteStatements),
    sqliteMilliseconds: duration(item.sqliteMilliseconds),
    postgresOperations: count(item.postgresOperations),
    postgresMilliseconds: duration(item.postgresMilliseconds)
  };
}

export function parsePerformanceSnapshot(value: unknown): PerformanceSnapshot {
  const item = record(value);
  if (typeof item.enabled !== 'boolean' || !Array.isArray(item.operations)) {
    throw new Error('Invalid performance snapshot');
  }
  const retentionMinutes = count(item.retentionMinutes);
  const capacity = count(item.capacity);
  const samples = count(item.samples);
  const operations = item.operations.map(parseOperation);
  if (!capacity || !retentionMinutes || samples > capacity || (!item.enabled && samples !== 0) ||
      operations.reduce((sum, operation) => sum + operation.requests, 0) !== samples) {
    throw new Error('Invalid performance retention');
  }
  return { enabled: item.enabled, retentionMinutes, capacity, samples, operations };
}

export function performanceQueryOptions() {
  return queryOptions({
    queryKey: ['system/performance', window.Readarr.apiRoot],
    queryFn: ({ signal }) => fetchJson({ path: '/system/performance', parse: parsePerformanceSnapshot, signal }),
    refetchOnWindowFocus: false,
    staleTime: 0,
    gcTime: 0
  });
}
