import { useQuery } from '@tanstack/react-query';
import React from 'react';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import translate from 'Utilities/String/translate';
import { performanceQueryOptions } from './performanceProfileQuery';
import styles from './PerformanceProfiling.css';

const milliseconds = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 });

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
          {snapshot.samples === 0 ? <p>{translate('PerformanceDiagnosticsEmpty')}</p> :
            <div className={styles.tableScroll}>
              <table className={styles.table}>
                <thead>
                  <tr><th scope="col">{translate('PerformanceDiagnosticsOperation')}</th><th scope="col">{translate('PerformanceDiagnosticsRequests')}</th><th scope="col">{translate('PerformanceDiagnosticsMean')}</th><th scope="col">{translate('PerformanceDiagnosticsMax')}</th><th scope="col">{translate('PerformanceDiagnosticsSqlite')}</th><th scope="col">{translate('PerformanceDiagnosticsPostgres')}</th><th scope="col">{translate('PerformanceDiagnosticsFailures')}</th></tr>
                </thead>
                <tbody>
                  {snapshot.operations.map((operation) => (
                    <tr key={`${operation.method}:${operation.operation}`}>
                      <th scope="row">{operation.method} {operation.operation}</th>
                      <td>{operation.requests}</td>
                      <td>{milliseconds.format(operation.meanMilliseconds)}</td>
                      <td>{milliseconds.format(operation.maxMilliseconds)}</td>
                      <td>{operation.sqliteStatements} / {milliseconds.format(operation.sqliteMilliseconds)}</td>
                      <td>{operation.postgresOperations} / {milliseconds.format(operation.postgresMilliseconds)}</td>
                      <td>{operation.failed} / {operation.cancelled}</td>
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
