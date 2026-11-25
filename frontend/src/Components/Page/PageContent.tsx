import React from 'react';
import ErrorBoundary from 'Components/Error/ErrorBoundary';
import DocumentTitle from './DocumentTitle';
import PageContentError from './PageContentError';
import styles from './PageContent.css';

interface PageContentProps {
  className?: string;
  title?: string;
  children: React.ReactNode;
}

function PageContent({ className = styles.content, title, children }: PageContentProps) {
  const instanceName = window.Readarr.instanceName;

  return (
    <ErrorBoundary renderError={(failure) => <PageContentError {...failure} />}>
      <DocumentTitle title={title ? `${title} - ${instanceName}` : instanceName}>
        <div className={className}>{children}</div>
      </DocumentTitle>
    </ErrorBoundary>
  );
}

export default PageContent;
