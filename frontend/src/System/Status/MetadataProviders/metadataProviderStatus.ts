import fetchJson from 'Utilities/Fetch/fetchJson';

export const operations = {
  authorCatalog: 'MetadataProviderAuthorCatalog',
  bookSearch: 'MetadataProviderBookSearch',
  authorSearch: 'MetadataProviderAuthorSearch',
  authorProfile: 'MetadataProviderAuthorProfile',
  recordingDetails: 'MetadataProviderRecordingDetails',
  chapters: 'Chapters',
  authorRefresh: 'MetadataProviderAuthorRefresh',
  recordingRefresh: 'MetadataProviderRecordingRefresh',
};

export const states = {
  complete: 'MetadataProviderComplete',
  cached: 'MetadataProviderCached',
  partial: 'MetadataProviderPartial',
  rateLimited: 'MetadataProviderRateLimited',
  unavailable: 'MetadataProviderUnavailable',
  missing: 'Missing',
  identityReview: 'MetadataProviderIdentityReview',
  cancelled: 'MetadataProviderCancelled',
};

export const reasons = {
  keywordCoverage: 'MetadataProviderKeywordCoverage',
  sparseMetadata: 'MetadataProviderSparseMetadata',
  repeatedPage: 'MetadataProviderRepeatedPage',
  incompletePagination: 'MetadataProviderIncompletePagination',
  catalogLimit: 'MetadataProviderCatalogLimit',
  tooManyRequests: 'MetadataProviderTooManyRequests',
  timeout: 'MetadataProviderTimeout',
  serverError: 'MetadataProviderServerError',
  httpError: 'MetadataProviderHttpError',
  invalidResponse: 'MetadataProviderInvalidResponse',
  configuration: 'MetadataProviderConfiguration',
  transport: 'MetadataProviderTransport',
  identityConflict: 'MetadataProviderIdentityConflict',
  confirmedMissing: 'MetadataProviderConfirmedMissing',
};

export interface MetadataProviderStatus {
  provider: 'audnexus' | 'audible';
  operation: keyof typeof operations;
  state: keyof typeof states;
  reason?: keyof typeof reasons | null;
  observedAtUtc: string;
  lastCompleteAtUtc?: string | null;
  retryAfterUtc?: string | null;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

function isKey<T extends object>(value: unknown, labels: T): value is keyof T {
  return typeof value === 'string' && Object.prototype.hasOwnProperty.call(labels, value);
}

function isDateTime(value: unknown): value is string {
  if (
    typeof value !== 'string' ||
    !/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(value) ||
    !Number.isFinite(Date.parse(value))
  ) {
    return false;
  }

  const day = value.slice(0, 10);
  return new Date(`${day}T00:00:00Z`).getUTCDate() === Number(day.slice(-2));
}

function isOptional<T>(value: unknown, isValid: (value: unknown) => value is T): value is T | null | undefined {
  return value == null || isValid(value);
}

function parseObservation(value: unknown): MetadataProviderStatus {
  if (!isObject(value)) {
    throw new TypeError('Invalid metadata observation');
  }

  const {
    provider,
    operation,
    state,
    reason,
    observedAtUtc,
    lastCompleteAtUtc,
    retryAfterUtc,
  } = value;

  if (
    (provider !== 'audnexus' && provider !== 'audible') ||
    !isKey(operation, operations) ||
    !isKey(state, states) ||
    !isOptional(reason, (value): value is keyof typeof reasons => isKey(value, reasons)) ||
    !isDateTime(observedAtUtc) ||
    !isOptional(lastCompleteAtUtc, isDateTime) ||
    !isOptional(retryAfterUtc, isDateTime)
  ) {
    throw new TypeError('Invalid metadata observation');
  }

  return { provider, operation, state, reason, observedAtUtc, lastCompleteAtUtc, retryAfterUtc };
}

export function parseMetadataProviderStatus(value: unknown): MetadataProviderStatus[] {
  if (!Array.isArray(value)) {
    throw new TypeError('Invalid metadata provider status');
  }

  return value.map(parseObservation);
}

export function fetchMetadataProviderStatus(signal?: AbortSignal) {
  return fetchJson({
    path: '/metadataproviderstatus',
    parse: parseMetadataProviderStatus,
    signal,
    timeout: 30000,
  });
}
