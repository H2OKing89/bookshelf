import { queryOptions } from '@tanstack/react-query';
import fetchJson from 'Utilities/Fetch/fetchJson';

interface PerformanceTiming {
  failed: number;
  cancelled: number;
  meanMilliseconds: number;
  maxMilliseconds: number;
  sqliteStatements: number;
  sqliteMilliseconds: number;
  postgresOperations: number;
  postgresMilliseconds: number;
}

export interface PerformanceOperation extends PerformanceTiming {
  operation: string;
  method: string;
  requests: number;
}

export type PerformanceJobCategory = 'authorRefresh' | 'bookRefresh' | 'scan' | 'import' | 'downloadMonitor' | 'mediaInfo';
export type PerformanceJobTrigger = 'scheduled' | 'manual' | 'unspecified';

export interface PerformanceJob extends PerformanceTiming {
  operation: string;
  category: PerformanceJobCategory;
  trigger: PerformanceJobTrigger;
  executions: number;
}

const jobCategories: Record<string, PerformanceJobCategory> = {
  RefreshAuthor: 'authorRefresh',
  BulkRefreshAuthor: 'authorRefresh',
  RefreshBook: 'bookRefresh',
  BulkRefreshBook: 'bookRefresh',
  RescanFolders: 'scan',
  ManualImport: 'import',
  DownloadedBooksScan: 'import',
  ProcessMonitoredDownloads: 'import',
  RefreshMonitoredDownloads: 'downloadMonitor',
  CheckForFinishedDownload: 'downloadMonitor',
  RefreshMediaInfo: 'mediaInfo',
  RefreshStaleMediaInfo: 'mediaInfo'
};

export interface PerformanceSnapshot {
  enabled: boolean;
  retentionMinutes: number;
  capacity: number;
  samples: number;
  operations: PerformanceOperation[];
  jobSamples: number;
  jobs: PerformanceJob[];
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

function parseTiming(item: Record<string, unknown>, samples: number): PerformanceTiming {
  const failed = count(item.failed);
  const cancelled = count(item.cancelled);
  const meanMilliseconds = duration(item.meanMilliseconds);
  const maxMilliseconds = duration(item.maxMilliseconds);
  if (samples === 0 || failed > samples || cancelled > samples || maxMilliseconds < meanMilliseconds) {
    throw new Error('Invalid performance timing');
  }
  return {
    failed,
    cancelled,
    meanMilliseconds,
    maxMilliseconds,
    sqliteStatements: count(item.sqliteStatements),
    sqliteMilliseconds: duration(item.sqliteMilliseconds),
    postgresOperations: count(item.postgresOperations),
    postgresMilliseconds: duration(item.postgresMilliseconds)
  };
}

function parseOperation(value: unknown): PerformanceOperation {
  const item = record(value);
  if (typeof item.operation !== 'string' || !/^[A-Za-z][A-Za-z0-9_.]*$/.test(item.operation) ||
      typeof item.method !== 'string' || !['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS', 'OTHER'].includes(item.method)) {
    throw new Error('Invalid performance operation');
  }
  const requests = count(item.requests);
  return { operation: item.operation, method: item.method, requests, ...parseTiming(item, requests) };
}

function parseJob(value: unknown): PerformanceJob {
  const item = record(value);
  if (typeof item.operation !== 'string' || !Object.prototype.hasOwnProperty.call(jobCategories, item.operation) ||
      item.category !== jobCategories[item.operation] ||
      (item.trigger !== 'scheduled' && item.trigger !== 'manual' && item.trigger !== 'unspecified')) {
    throw new Error('Invalid performance job');
  }
  const executions = count(item.executions);
  return {
    operation: item.operation,
    category: jobCategories[item.operation],
    trigger: item.trigger,
    executions,
    ...parseTiming(item, executions)
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
  // An older backend may omit both job fields. Retain the request contract,
  // but reject a partial or malformed job response from a newer backend.
  let jobSamples = 0;
  let jobs: PerformanceJob[] = [];
  if (item.jobSamples !== undefined || item.jobs !== undefined) {
    if (!Array.isArray(item.jobs)) {
      throw new Error('Invalid performance jobs');
    }
    jobSamples = count(item.jobSamples);
    jobs = item.jobs.map(parseJob);
  }
  if (!capacity || !retentionMinutes || samples + jobSamples > capacity || (!item.enabled && samples + jobSamples !== 0) ||
      operations.reduce((sum, operation) => sum + operation.requests, 0) !== samples ||
      jobs.reduce((sum, job) => sum + job.executions, 0) !== jobSamples) {
    throw new Error('Invalid performance retention');
  }
  return { enabled: item.enabled, retentionMinutes, capacity, samples, operations, jobSamples, jobs };
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
