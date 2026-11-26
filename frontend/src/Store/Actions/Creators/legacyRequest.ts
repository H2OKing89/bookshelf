import type { StateRecord } from 'Store/StateTypes';
import createAjaxRequest from 'Utilities/createAjaxRequest';

interface RequestOptions extends StateRecord {
  url: string;
  method?: string;
  data?: unknown;
  dataType?: string;
  contentType?: string | false;
  traditional?: boolean;
}

export interface LegacyRequest {
  done(callback: (data: unknown) => void): LegacyRequest;
  fail(callback: (xhr: unknown) => void): LegacyRequest;
}

// The legacy transport remains JavaScript. Consumers validate data at its
// boundary instead of propagating unchecked response objects to reducers.
export function requestLegacy(options: RequestOptions): { request: LegacyRequest; abortRequest: () => void } {
  return createAjaxRequest(options);
}
