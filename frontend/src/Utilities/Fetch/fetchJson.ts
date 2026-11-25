export type ApiErrorBody =
  | { format: 'empty' }
  | { format: 'json'; value: unknown }
  | { format: 'text'; value: string };

type ApiErrorDetails =
  | { kind: 'http'; status: number; statusText: string; body: ApiErrorBody }
  | { kind: 'aborted' | 'timeout' | 'network' | 'invalid-request' | 'invalid-response' };

const errorMessages = {
  aborted: 'The request was cancelled.',
  timeout: 'The request timed out.',
  network: 'The network request failed.',
  'invalid-request': 'The request is invalid.',
  'invalid-response': 'The response is invalid.',
};

export class ApiError extends Error {
  constructor(public readonly details: ApiErrorDetails) {
    super(
      details.kind === 'http'
        ? `HTTP ${details.status}`
        : errorMessages[details.kind]
    );
    this.name = 'ApiError';
  }

  get kind() {
    return this.details.kind;
  }
}

export interface FetchJsonOptions<T, TBody = unknown> {
  path: string;
  apiVersion?: 2;
  parse(value: unknown): T;
  method?: string;
  headers?: HeadersInit;
  body?: TBody;
  signal?: AbortSignal;
  timeout?: number;
}

function requestTarget(path: string, apiVersion?: 2) {
  const origin = window.location.origin;

  if (/^(https?:)?\/\//i.test(path)) {
    const url = new URL(path, origin);

    if (apiVersion !== undefined || url.username || url.password) {
      throw new ApiError({ kind: 'invalid-request' });
    }

    return { url, isApi: false };
  }

  const root = new URL(window.Readarr.apiRoot, origin);
  let prefix = root.pathname.replace(/\/+$/, '');

  if (apiVersion !== undefined) {
    if (apiVersion !== 2 || !prefix.endsWith('/api/v1')) {
      throw new ApiError({ kind: 'invalid-request' });
    }

    prefix = `${prefix.slice(0, -1)}2`;
  }

  if (
    root.origin !== origin ||
    !prefix ||
    root.username ||
    root.password ||
    root.search ||
    root.hash ||
    !path.startsWith('/') ||
    path.includes('\\')
  ) {
    throw new ApiError({ kind: 'invalid-request' });
  }

  const url = new URL(prefix + path, origin);

  if (url.origin !== origin || !url.pathname.startsWith(`${prefix}/`) || url.hash) {
    throw new ApiError({ kind: 'invalid-request' });
  }

  return { url, isApi: true };
}

function prepareRequest(path: string, requestHeaders: HeadersInit | undefined, body: unknown, apiVersion?: 2) {
  try {
    const target = requestTarget(path, apiVersion);
    const headers = new Headers(requestHeaders);
    const json = body === undefined ? undefined : JSON.stringify(body);

    if (body !== undefined && json === undefined) {
      throw new ApiError({ kind: 'invalid-request' });
    }

    headers.set('Accept', 'application/json');

    if (json !== undefined) {
      headers.set('Content-Type', 'application/json');
    }

    if (target.isApi) {
      headers.set('X-Api-Key', window.Readarr.apiKey);
      headers.set('X-Requested-With', 'XMLHttpRequest');
    } else {
      headers.delete('X-Api-Key');
      headers.delete('X-Requested-With');
    }

    return { ...target, headers, json };
  } catch {
    throw new ApiError({ kind: 'invalid-request' });
  }
}

function errorBody(text: string | undefined): ApiErrorBody {
  if (text == null || !text.trim()) {
    return { format: 'empty' };
  }

  try {
    return { format: 'json', value: JSON.parse(text) };
  } catch {
    return { format: 'text', value: text };
  }
}

function parseResponse<T>(text: string | undefined, parse: (value: unknown) => T) {
  try {
    const data: unknown = text === undefined ? undefined : JSON.parse(text);
    return parse(data);
  } catch {
    throw new ApiError({ kind: 'invalid-response' });
  }
}

async function fetchJson<T, TBody = unknown>({
  path,
  apiVersion,
  parse,
  method,
  headers: requestHeaders,
  body,
  signal,
  timeout,
}: FetchJsonOptions<T, TBody>): Promise<T> {
  if (signal?.aborted) {
    throw new ApiError({ kind: 'aborted' });
  }

  const { url, isApi, headers, json } = prepareRequest(path, requestHeaders, body, apiVersion);

  const controller = new AbortController();
  const onAbort = () => controller.abort('aborted');
  signal?.addEventListener('abort', onAbort);
  const timer = timeout === undefined
    ? undefined
    : setTimeout(() => controller.abort('timeout'), timeout);

  try {
    const response = await fetch(url, {
      method,
      headers,
      body: json,
      credentials: isApi ? 'same-origin' : 'omit',
      // Redirects could forward the API key to a different origin.
      redirect: 'error',
      signal: controller.signal,
    });
    const contents = response.status === 204 ? undefined : await response.text();

    if (controller.signal.aborted) {
      throw new ApiError({ kind: 'aborted' });
    }

    if (!response.ok) {
      throw new ApiError({
        kind: 'http',
        status: response.status,
        statusText: response.statusText,
        body: errorBody(contents),
      });
    }

    return parseResponse(contents, parse);
  } catch (error) {
    if (controller.signal.aborted) {
      throw new ApiError({
        kind: controller.signal.reason === 'timeout' ? 'timeout' : 'aborted',
      });
    }

    if (error instanceof ApiError) {
      throw error;
    }

    throw new ApiError({ kind: 'network' });
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener('abort', onAbort);
  }
}

export default fetchJson;
