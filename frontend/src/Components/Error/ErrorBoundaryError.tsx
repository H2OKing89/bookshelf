import React, { useEffect, useState } from 'react';
import StackTrace from 'stacktrace-js';
import translate from 'Utilities/String/translate';
import styles from './ErrorBoundaryError.css';

interface ErrorBoundaryErrorProps {
  className?: string;
  messageClassName?: string;
  detailsClassName?: string;
  message?: string;
  error: Error;
  info: React.ErrorInfo;
}

function ErrorBoundaryError(props: ErrorBoundaryErrorProps) {
  const {
    className = styles.container,
    messageClassName = styles.message,
    detailsClassName = styles.details,
    message = translate('ErrorLoadingContent'),
    error,
    info,
  } = props;

  const [detailedError, setDetailedError] = useState<
    StackTrace.StackFrame[] | null
  >(null);

  useEffect(() => {
    let cancelled = false;
    setDetailedError(null);
    if (error) {
      StackTrace.fromError(error).then((frames) => {
        if (!cancelled) {
          setDetailedError(frames);
        }
      }).catch(() => {
        // The component stack is still available if the source map cannot be read.
      });
    }

    return () => { cancelled = true; };
  }, [error, setDetailedError]);

  return (
    <div className={className}>
      <div className={messageClassName}>{message}</div>

      <div className={styles.imageContainer}>
        <img
          className={styles.image}
          alt=""
          src={`${window.Readarr.urlBase}/Content/Images/error.png`}
        />
      </div>

      <details className={detailsClassName}>
        {error ? <div>{error.message}</div> : null}

        {detailedError ? (
          detailedError.map((d, index) => {
            return (
              <div key={index}>
                {`  at ${d.functionName} (${d.fileName}:${d.lineNumber}:${d.columnNumber})`}
              </div>
            );
          })
        ) : (
          <div>{info.componentStack}</div>
        )}

        {
          <div className={styles.version}>
            Version: {window.Readarr.version}
          </div>
        }
      </details>
    </div>
  );
}

export default ErrorBoundaryError;
