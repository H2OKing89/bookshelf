import { useQuery } from '@tanstack/react-query';
import React from 'react';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import translate from 'Utilities/String/translate';
import { PerformanceJob, PerformanceJobCategory, PerformanceJobTrigger, PerformanceOperation, performanceQueryOptions } from './performanceProfileQuery';
import styles from './PerformanceProfiling.css';

const milliseconds = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 });
const categoryLabels: Record<PerformanceJobCategory, string> = {
  authorRefresh: 'PerformanceDiagnosticsAuthorRefresh',
  bookRefresh: 'PerformanceDiagnosticsBookRefresh',
  scan: 'PerformanceDiagnosticsScan',
  import: 'PerformanceDiagnosticsImport',
  downloadMonitor: 'PerformanceDiagnosticsDownloadMonitor',
  mediaInfo: 'PerformanceDiagnosticsMediaInfo'
};
const triggerLabels: Record<PerformanceJobTrigger, string> = {
  scheduled: 'PerformanceDiagnosticsScheduled',
  manual: 'PerformanceDiagnosticsManual',
  unspecified: 'PerformanceDiagnosticsInternal'
};

function TimingHeaders() {
  return (
    <>
      <th scope="col">{translate('PerformanceDiagnosticsMean')}</th>
      <th scope="col">{translate('PerformanceDiagnosticsMax')}</th>
      <th scope="col">{translate('PerformanceDiagnosticsSqlite')}</th>
      <th scope="col">{translate('PerformanceDiagnosticsPostgres')}</th>
      <th scope="col">{translate('PerformanceDiagnosticsFailures')}</th>
    </>
  );
}

function TimingCells({ timing }: { timing: PerformanceOperation | PerformanceJob }) {
  return (
    <>
      <td>{milliseconds.format(timing.meanMilliseconds)}</td>
      <td>{milliseconds.format(timing.maxMilliseconds)}</td>
      <td>{timing.sqliteStatements} / {milliseconds.format(timing.sqliteMilliseconds)}</td>
      <td>{timing.postgresOperations} / {milliseconds.format(timing.postgresMilliseconds)}</td>
      <td>{timing.failed} / {timing.cancelled}</td>
    </>
  );
}

function PerformanceProfiling() {
  const result = useQuery(performanceQueryOptions());
  const snapshot = result.data;
  return (
    <FieldSet legend={translate('PerformanceDiagnostics')}>
      {result.isPending && <LoadingIndicator />}
      {result.isError && <p role="alert">{translate('PerformanceDiagnosticsUnavailable')}</p>}
      {snapshot && !snapshot.enabled && <p>{translate('PerformanceDiagnosticsDisabled')}</p>}
      {snapshot?.enabled &&
        <>
          <p>{translate('PerformanceDiagnosticsRetention', { minutes: snapshot.retentionMinutes, capacity: snapshot.capacity })}</p>
          <Button isDisabled={result.isFetching} onPress={() => result.refetch()}>{translate('PerformanceDiagnosticsRefresh')}</Button>
          <h3>{translate('PerformanceDiagnosticsApiRequests')}</h3>
          {snapshot.samples === 0 ? <p>{translate('PerformanceDiagnosticsEmpty')}</p> :
            <div className={styles.tableScroll}>
              <table className={styles.table} aria-label={translate('PerformanceDiagnosticsApiRequests')}>
                <thead>
                  <tr>
                    <th scope="col">{translate('PerformanceDiagnosticsOperation')}</th>
                    <th scope="col">{translate('PerformanceDiagnosticsRequests')}</th>
                    <TimingHeaders />
                  </tr>
                </thead>
                <tbody>
                  {snapshot.operations.map((operation) => (
                    <tr key={`${operation.method}:${operation.operation}`}>
                      <th scope="row">{operation.method} {operation.operation}</th>
                      <td>{operation.requests}</td>
                      <TimingCells timing={operation} />
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>}
          <h3>{translate('PerformanceDiagnosticsBackgroundJobs')}</h3>
          {snapshot.jobSamples === 0 ? <p>{translate('PerformanceDiagnosticsJobsEmpty')}</p> :
            <div className={styles.tableScroll}>
              <table className={styles.table} aria-label={translate('PerformanceDiagnosticsBackgroundJobs')}>
                <thead>
                  <tr>
                    <th scope="col">{translate('PerformanceDiagnosticsOperation')}</th>
                    <th scope="col">{translate('PerformanceDiagnosticsCategory')}</th>
                    <th scope="col">{translate('PerformanceDiagnosticsTrigger')}</th>
                    <th scope="col">{translate('PerformanceDiagnosticsExecutions')}</th>
                    <TimingHeaders />
                  </tr>
                </thead>
                <tbody>
                  {snapshot.jobs.map((job) => (
                    <tr key={`${job.category}:${job.trigger}:${job.operation}`}>
                      <th scope="row">{job.operation}</th>
                      <td>{translate(categoryLabels[job.category])}</td>
                      <td>{translate(triggerLabels[job.trigger])}</td>
                      <td>{job.executions}</td>
                      <TimingCells timing={job} />
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>}
        </>}
    </FieldSet>
  );
}

export default PerformanceProfiling;
