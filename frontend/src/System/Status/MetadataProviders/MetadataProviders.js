import PropTypes from 'prop-types';
import React, { useEffect, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import styles from './MetadataProviders.css';

const operations = {
  authorCatalog: 'MetadataProviderAuthorCatalog',
  bookSearch: 'MetadataProviderBookSearch',
  authorSearch: 'MetadataProviderAuthorSearch',
  authorProfile: 'MetadataProviderAuthorProfile',
  recordingDetails: 'MetadataProviderRecordingDetails',
  chapters: 'Chapters',
  authorRefresh: 'MetadataProviderAuthorRefresh',
  recordingRefresh: 'MetadataProviderRecordingRefresh'
};

const states = {
  complete: 'MetadataProviderComplete',
  cached: 'MetadataProviderCached',
  partial: 'MetadataProviderPartial',
  rateLimited: 'MetadataProviderRateLimited',
  unavailable: 'MetadataProviderUnavailable',
  missing: 'Missing',
  identityReview: 'MetadataProviderIdentityReview',
  cancelled: 'MetadataProviderCancelled'
};

const reasons = {
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
  confirmedMissing: 'MetadataProviderConfirmedMissing'
};

const localTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });

function StatusTime({ label, value }) {
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

StatusTime.propTypes = {
  label: PropTypes.string.isRequired,
  value: PropTypes.string
};

function MetadataProviders() {
  const [items, setItems] = useState(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let request = null;
    let mounted = true;

    function readStatus() {
      if (request) {
        return;
      }

      request = createAjaxRequest({ url: '/metadataproviderstatus', dataType: 'json' });
      request.request.done((result) => {
        if (mounted) {
          setItems(result);
          setFailed(false);
        }
      }).fail(() => {
        if (mounted) {
          setFailed(true);
        }
      }).always(() => {
        request = null;
      });
    }

    readStatus();
    const refresh = setInterval(readStatus, 30000);
    return () => {
      mounted = false;
      clearInterval(refresh);
      request?.abortRequest();
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
                    <span className={styles.operation}>{translate(operations[operation] || 'Unknown')}</span>
                  </div>
                  <strong>{translate(states[state] || 'Unknown')}</strong>
                </div>
                {
                  !!reasons[reason] &&
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
