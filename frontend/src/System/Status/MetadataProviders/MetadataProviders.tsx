import { useQuery } from '@tanstack/react-query';
import React from 'react';
import FieldSet from 'Components/FieldSet';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import translate from 'Utilities/String/translate';
import {
  operations,
  reasons,
  states,
} from './metadataProviderStatus';
import metadataProviderStatusQueryOptions from './metadataProviderStatusQueryOptions';
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
  const { data: items, isError: failed, isPending } = useQuery(metadataProviderStatusQueryOptions());

  return (
    <FieldSet legend={<h2 className={styles.heading}>{translate('MetadataProviders')}</h2>}>
      {isPending && <LoadingIndicator />}
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
