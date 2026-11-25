import { useQuery } from '@tanstack/react-query';
import React from 'react';
import { useSelector } from 'react-redux';
import PageSidebarStatus from 'Components/Page/Sidebar/PageSidebarStatus';
import { queueStatusQuery } from '../queuePageQuery';
import { useIncludeUnknownAuthorItems } from '../queuePreferences';

export default function QueueStatusConnector() {
  const includeUnknownAuthorItems = useIncludeUnknownAuthorItems();
  const isConnected = useSelector((state) => state.app.isConnected);
  const isReconnecting = useSelector((state) => state.app.isReconnecting);
  const query = useQuery(queueStatusQuery());
  const status = query.data;

  return (
    <PageSidebarStatus
      isConnected={isConnected}
      isReconnecting={isReconnecting}
      isPopulated={!!status}
      count={(includeUnknownAuthorItems ? status?.totalCount : status?.count) ?? 0}
      errors={!!(status?.errors || (includeUnknownAuthorItems && status?.unknownErrors))}
      warnings={!!(status?.warnings || (includeUnknownAuthorItems && status?.unknownWarnings))}
    />
  );
}
