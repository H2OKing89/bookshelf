import React, { useEffect, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import translate from 'Utilities/String/translate';
import {
  fetchMetadataProviderStatus,
  MetadataProviderStatus,
  operations,
  reasons,
  states,
} from './metadataProviderStatus';
import styles from './MetadataProviders.css';

const localTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });

function StatusTime({ label, value }: { label: string; value?: string | null }) {
  if (!value) {
    return null;
  }

  return (
    <div>
      <dt>{translate(label)}</dt>
      <dd>
        <time dateTime={value}>{localTime.format(new Date(value))}</time>
      </dd>
    </div>
  );
}

function MetadataProviders() {
  const [items, setItems] = useState<MetadataProviderStatus[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let request: AbortController | null = null;

    async function readStatus() {
      if (request) {
        return;
      }

      const controller = new AbortController();
      request = controller;

      try {
        const result = await fetchMetadataProviderStatus(controller.signal);
        if (!controller.signal.aborted) {
          setItems(result);
          setFailed(false);
        }
      } catch {
        if (!controller.signal.aborted) {
          setFailed(true);
        }
      } finally {
        request = null;
      }
    }

    readStatus();
    const refresh = setInterval(readStatus, 30000);
    return () => {
      clearInterval(refresh);
      request?.abort();
    };
  }, []);

  return (
    <FieldSet legend={<h2 className={styles.heading}>{translate('MetadataProviders')}</h2>}>
      {items === null && !failed && <LoadingIndicator />}
      {failed && <p role="alert">{translate('MetadataProviderStatusUnavailable')}</p>}
      {items?.length === 0 && !failed && <p>{translate('MetadataProviderNoObservations')}</p>}
      {
        !!items?.length &&
          <ul className={styles.observations}>
            {items.map(({ provider, operation, state, reason, observedAtUtc, lastCompleteAtUtc, retryAfterUtc }) => (
              <li className={styles.observation} key={`${provider}-${operation}`}>
                <div className={styles.result}>
                  <div>
                    <strong>{provider === 'audnexus' ? 'Audnexus' : translate('MetadataProviderAudible')}</strong>
                    <span className={styles.operation}>{translate(operations[operation])}</span>
                  </div>
                  <strong>{translate(states[state])}</strong>
                </div>
                {
                  reason &&
                    <p className={styles.reason}>{translate(reasons[reason])}</p>
                }
                <dl className={styles.times}>
                  <StatusTime label="MetadataProviderObservedAt" value={observedAtUtc} />
                  <StatusTime label="MetadataProviderLastCompleteAt" value={lastCompleteAtUtc} />
                  <StatusTime label="MetadataProviderRetryAfter" value={retryAfterUtc} />
                </dl>
              </li>
            ))}
          </ul>
      }
    </FieldSet>
  );
}

export default MetadataProviders;
