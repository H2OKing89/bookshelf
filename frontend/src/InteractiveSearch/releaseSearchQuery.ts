import { type QueryClient,queryOptions } from '@tanstack/react-query';
import queryClient from 'App/queryClient';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';

export const releaseFreshnessMs = 5 * 60 * 1000;
const releaseCacheMs = 10 * 60 * 1000;
const releaseRequestTimeout = 120_000;

export type ReleaseSearchScope = { bookId: number } | { authorId: number } | Record<string, never>;

interface ReleaseIdentity {
  guid: string;
  indexerId: number;
  searchResultId: string;
}

export interface SearchRelease extends ReleaseIdentity, Record<string, unknown> {
  title: string;
  rejections: string[];
  customFormats: unknown[];
  isGrabbing?: boolean;
  isGrabbed?: boolean;
  grabError?: string | null;
}

export interface ReleaseSearchResults {
  fetchedAt: number;
  items: SearchRelease[];
  refreshRequired?: boolean;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function validId(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 && value <= 2_147_483_647;
}

export function parseReleaseSearchScope(value: unknown): ReleaseSearchScope {
  if (!isRecord(value)) {
    throw new Error('Invalid interactive search scope');
  }

  const { bookId, authorId } = value;
  if (validId(bookId) && authorId == null) {
    return { bookId };
  }
  if (validId(authorId) && bookId == null) {
    return { authorId };
  }
  if (bookId == null && authorId == null) {
    return {};
  }

  throw new Error('Invalid interactive search scope');
}

function parseReleaseIdentity(value: unknown): ReleaseIdentity {
  if (!isRecord(value) || typeof value.guid !== 'string' || !value.guid || !validId(value.indexerId) ||
      typeof value.searchResultId !== 'string' || !/^[a-f0-9]{32}$/i.test(value.searchResultId)) {
    throw new Error('Invalid search result identity');
  }

  return { guid: value.guid, indexerId: value.indexerId, searchResultId: value.searchResultId };
}

export function sameRelease(left: ReleaseIdentity, right: ReleaseIdentity) {
  return left.searchResultId === right.searchResultId && left.guid === right.guid && left.indexerId === right.indexerId;
}

function parseSearchResults(value: unknown): SearchRelease[] {
  if (!Array.isArray(value)) {
    throw new Error('Invalid interactive search results');
  }

  const seen = new Set<string>();
  return value.map((item: unknown) => {
    const identity = parseReleaseIdentity(item);
    if (seen.has(identity.searchResultId) || !isRecord(item) || typeof item.title !== 'string' ||
        !Array.isArray(item.rejections) || !item.rejections.every((reason: unknown) => typeof reason === 'string') ||
        !isRecord(item.quality) || !isRecord(item.quality.quality) || (!validId(item.quality.quality.id) && item.quality.quality.id !== 0) ||
        typeof item.quality.quality.name !== 'string' || !isRecord(item.quality.revision) || typeof item.downloadAllowed !== 'boolean') {
      throw new Error('Invalid interactive search result');
    }

    seen.add(identity.searchResultId);
    const rejections: string[] = item.rejections;
    const customFormats: unknown[] = Array.isArray(item.customFormats) ? item.customFormats : [];
    if (!customFormats.every(format => isRecord(format) && validId(format.id) && typeof format.name === 'string')) {
      throw new Error('Invalid search release formats');
    }

    return { ...item, ...identity, title: item.title, customFormats, rejections };
  });
}

export function releaseSearchFamilyKey() {
  return ['interactiveRelease', window.Readarr.apiRoot] as const;
}

export function releaseSearchKey(scope: ReleaseSearchScope) {
  return [...releaseSearchFamilyKey(), scope] as const;
}

export type ReleaseSearchKey = ReturnType<typeof releaseSearchKey>;

export default function releaseSearchQuery(scope: ReleaseSearchScope) {
  const params = new URLSearchParams();
  for (const [name, id] of Object.entries(scope)) {
    params.set(name, String(id));
  }

  return queryOptions({
    queryKey: releaseSearchKey(scope),
    queryFn: async ({ signal }): Promise<ReleaseSearchResults> => ({
      items: await fetchJson({ path: `/release${params.size ? `?${params}` : ''}`, parse: parseSearchResults, signal, timeout: releaseRequestTimeout }),
      fetchedAt: Date.now()
    }),
    staleTime: releaseFreshnessMs,
    gcTime: releaseCacheMs,
    networkMode: 'always',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
}

export function filterReleaseTitles(items: SearchRelease[], text: string) {
  const search = text.trim().toLocaleLowerCase();
  return search ? items.filter(item => item.title.toLocaleLowerCase().includes(search)) : items;
}

export function invalidateReleaseSearches(scope?: ReleaseSearchScope) {
  return queryClient.invalidateQueries({ queryKey: scope ? releaseSearchKey(scope) : releaseSearchFamilyKey(), refetchType: 'none' });
}

export function currentRelease(client: QueryClient, key: ReleaseSearchKey, action: unknown) {
  let identity: ReleaseIdentity | undefined = undefined;
  try {
    identity = parseReleaseIdentity(action);
  } catch {
    return undefined;
  }

  const state = client.getQueryState<ReleaseSearchResults>(key);
  if (key[1] !== window.Readarr.apiRoot || state?.status !== 'success' || state.fetchStatus !== 'idle' || state.isInvalidated ||
      !state.data || state.data.refreshRequired || Date.now() - state.data.fetchedAt >= releaseFreshnessMs) {
    return undefined;
  }

  return state.data.items.find(item => sameRelease(item, identity));
}

export interface GrabSearchRelease {
  key: ReleaseSearchKey;
  identity: ReleaseIdentity;
}

export function grabSearchRelease({ key, identity }: GrabSearchRelease) {
  const { guid, indexerId, searchResultId } = identity;
  return fetchJson({
    path: '/release', method: 'POST', body: { ...key[2], guid, indexerId, searchResultId }, timeout: releaseRequestTimeout,
    parse: (value) => {
      if (!sameRelease(parseReleaseIdentity(value), identity)) {
        throw new Error('Release response did not match the selected result');
      }
    }
  });
}

export function setReleaseStatus(client: QueryClient, { key, identity }: GrabSearchRelease,
  status: Pick<SearchRelease, 'isGrabbing' | 'isGrabbed' | 'grabError'>, refreshRequired = false) {
  const data = client.getQueryData<ReleaseSearchResults>(key);
  if (!data?.items.some(item => sameRelease(item, identity))) {
    return;
  }

  const invalidated = client.getQueryState(key)?.isInvalidated;
  client.setQueryData<ReleaseSearchResults>(key, {
    ...data,
    refreshRequired: data.refreshRequired || refreshRequired || invalidated,
    items: data.items.map(item => sameRelease(item, identity) ? { ...item, ...status } : item)
  }, { updatedAt: data.fetchedAt });
}

export function releaseGrabError(error: Error) {
  if (error instanceof ApiError && error.details.kind === 'http') {
    const { status, body } = error.details;
    return {
      message: body.format === 'json' && isRecord(body.value) && typeof body.value.message === 'string'
        ? body.value.message : 'Failed to add to download queue',
      refreshRequired: status === 404
    };
  }

  return { message: 'Failed to add to download queue', refreshRequired: false };
}
