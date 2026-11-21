import { queryOptions } from '@tanstack/react-query';
import { fetchMetadataProviderStatus } from './metadataProviderStatus';

const refreshInterval = 30_000;

export default function metadataProviderStatusQueryOptions() {
  return queryOptions({
    queryKey: ['metadataProviderStatus', window.Readarr.apiRoot] as const,
    queryFn: ({ signal }) => fetchMetadataProviderStatus(signal),
    staleTime: refreshInterval,
    refetchInterval: refreshInterval,
    refetchIntervalInBackground: true,
    networkMode: 'always',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
}
