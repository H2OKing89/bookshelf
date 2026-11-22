import { isPreferenceRecord } from 'Store/Middleware/preferenceStorage';

export const queueStatuses = [
  ['queued', 'Queued'],
  ['paused', 'QueueStatusPaused'],
  ['downloading', 'QueueStatusDownloading'],
  ['completed', 'QueueStatusDownloaded'],
  ['failed', 'QueueStatusFailed'],
  ['warning', 'QueueStatusWarning'],
  ['delay', 'QueueStatusDelayed'],
  ['downloadClientUnavailable', 'QueueStatusClientUnavailable'],
  ['fallback', 'QueueStatusFallback']
] as const;

export const queueProtocols = [
  ['torrent', 'QueueProtocolTorrent'],
  ['usenet', 'Usenet'],
  ['unknown', 'Unknown']
] as const;

export interface QueueFilters {
  status: '' | typeof queueStatuses[number][0];
  protocol: '' | typeof queueProtocols[number][0];
}

export function normalizeQueueFilters(value: unknown): QueueFilters {
  const saved = isPreferenceRecord(value) ? value : {};

  return {
    status: queueStatuses.find(([key]) => key === saved.status)?.[0] ?? '',
    protocol: queueProtocols.find(([key]) => key === saved.protocol)?.[0] ?? ''
  };
}
