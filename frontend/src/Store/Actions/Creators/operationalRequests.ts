import { isStateRecord, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import requestAction from 'Utilities/requestAction';
import type { LegacyRequest } from './legacyRequest';

export function asPromise(request: LegacyRequest): Promise<unknown> {
  return new Promise((resolve, reject) => {
    request.done(resolve);
    request.fail(reject);
  });
}

export function providerRequest(payload: StateRecord): LegacyRequest {
  if (typeof payload.provider !== 'string' || !payload.provider ||
      typeof payload.action !== 'string' || !isStateRecord(payload.providerData)) {
    throw new ApiError({ kind: 'invalid-request' });
  }
  // Preserve the existing provider transport. Treat responses as unknown.
  return requestAction(payload);
}

export function requireRecord(value: unknown): StateRecord {
  if (!isStateRecord(value)) {
    throw new ApiError({ kind: 'invalid-response' });
  }
  return value;
}
