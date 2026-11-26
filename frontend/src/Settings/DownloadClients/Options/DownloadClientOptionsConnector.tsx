import { useIsMutating, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useLayoutEffect, useMemo, useState } from 'react';
import { useSelector } from 'react-redux';
import { announceSettingsSaved } from 'Components/ActionStatus';
import DownloadClientOptions from './DownloadClientOptions';
import {
  changeDownloadClientOption,
  type DownloadClientOptionsDraft,
  downloadClientOptionsQuery,
  isDownloadClientOption,
  saveDownloadClientOptions
} from './downloadClientOptionsState';

interface DownloadClientOptionsConnectorProps {
  onChildMounted: (save: (() => void) | null) => void;
  onChildStateChange: (state: {
    hasPendingChanges: boolean;
    isSaving: boolean;
    onDiscardPendingChanges: () => void;
  }) => void;
}

function DownloadClientOptionsConnector({ onChildMounted, onChildStateChange }: DownloadClientOptionsConnectorProps) {
  const apiRoot = window.Readarr.apiRoot;
  const advancedSettings = useSelector((state: { settings: { advancedSettings: boolean } }) => state.settings.advancedSettings);
  const queryClient = useQueryClient();
  const options = downloadClientOptionsQuery(apiRoot);
  const query = useQuery(options);
  const [draft, setDraft] = useState<DownloadClientOptionsDraft>({});
  const mutationFilter = useMemo(() => ({
    mutationKey: ['saveDownloadClientOptions', apiRoot], exact: true
  }), [apiRoot]);
  const isSaving = useIsMutating(mutationFilter) > 0;
  const { mutate, reset, error: saveError } = useMutation({
    mutationKey: mutationFilter.mutationKey,
    mutationFn: saveDownloadClientOptions,
    networkMode: 'always',
    retry: false,
    onMutate: () => queryClient.cancelQueries({ queryKey: options.queryKey, exact: true }),
    onSuccess: async (canonical) => {
      await queryClient.cancelQueries({ queryKey: options.queryKey, exact: true });
      queryClient.setQueryData(options.queryKey, canonical);
    }
  });

  const saved = query.isSuccess && !query.isFetching ? query.data : undefined;
  const hasPendingChanges = Object.keys(draft).length !== 0;
  const onDiscardPendingChanges = useCallback(() => {
    reset();
    setDraft({});
  }, [reset]);

  const onSave = useCallback(() => {
    if (saved && hasPendingChanges && !queryClient.isMutating(mutationFilter)) {
      mutate({ ...saved, ...draft }, {
        onSuccess: () => {
          setDraft({});
          announceSettingsSaved();
        }
      });
    }
  }, [draft, hasPendingChanges, mutate, mutationFilter, queryClient, saved]);

  useLayoutEffect(() => {
    onChildMounted(onSave);

    return () => onChildMounted(null);
  }, [onChildMounted, onSave]);

  useLayoutEffect(() => {
    onChildStateChange({ hasPendingChanges, isSaving, onDiscardPendingChanges });
  }, [hasPendingChanges, isSaving, onChildStateChange, onDiscardPendingChanges]);

  const onInputChange = useCallback(({ name, value }: { name: string; value: unknown }) => {
    if (saved && typeof value === 'boolean' && isDownloadClientOption(name) && !queryClient.isMutating(mutationFilter)) {
      reset();
      setDraft((current) => changeDownloadClientOption(saved, current, name, value));
    }
  }, [mutationFilter, queryClient, reset, saved]);

  return (
    <DownloadClientOptions
      advancedSettings={advancedSettings}
      isFetching={query.isPending || query.isFetching}
      loadError={query.isError}
      isSaving={isSaving}
      saveError={saveError}
      saved={saved}
      draft={draft}
      onInputChange={onInputChange}
    />
  );
}

export default DownloadClientOptionsConnector;
