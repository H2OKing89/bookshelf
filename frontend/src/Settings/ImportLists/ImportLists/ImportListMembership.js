import { useQuery } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useState } from 'react';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import fetchJson from 'Utilities/Fetch/fetchJson';

function parseSnapshot(value) {
  const textFields = ['sourceAuthor', 'sourceTitle', 'sourceAuthorId', 'sourceBookId', 'sourceEditionId', 'authorId', 'bookId', 'editionId'];
  if (!value || !Number.isInteger(value.importListId) || typeof value.isCurrent !== 'boolean' || !Array.isArray(value.items) ||
    (value.lastSuccessfulSync != null && (typeof value.lastSuccessfulSync !== 'string' || Number.isNaN(Date.parse(value.lastSuccessfulSync)))) ||
    value.items.some((item) => !item || typeof item.excluded !== 'boolean' || textFields.some((field) => item[field] != null && typeof item[field] !== 'string'))) {
    throw new Error('Invalid list membership response');
  }
  return value;
}

function membershipStatus(item) {
  if (item.excluded) {
    return 'Excluded';
  }
  const isBook = item.sourceTitle || item.sourceBookId || item.sourceEditionId;
  const resolved = isBook ? item.bookId && item.editionId === item.bookId && item.authorId : item.authorId;
  return resolved ? 'Resolved identity' : 'Unresolved identity';
}

function ImportListMembership({ id }) {
  const [isOpen, setIsOpen] = useState(false);
  const [visible, setVisible] = useState(50);
  const query = useQuery({
    queryKey: ['importlistmembership', window.Readarr.apiRoot, id],
    queryFn: ({ signal }) => fetchJson({ path: `/importlistmembership/${id}`, parse: parseSnapshot, signal }),
    staleTime: 0,
    enabled: isOpen
  });
  const snapshot = query.data;

  return (
    <details onToggle={(event) => setIsOpen(event.currentTarget.open)}>
      <summary>List membership</summary>
      <p>Last successful provider membership, including excluded and unresolved entries. This is not a live provider request.</p>
      <Button onPress={() => query.refetch()} isDisabled={query.isFetching}>Refresh membership</Button>
      {isOpen && query.isPending && <LoadingIndicator />}
      {query.isError && <p role="alert">Unable to load list membership.</p>}
      {snapshot &&
        <>
          <p>Last successful sync: {snapshot.lastSuccessfulSync ? new Date(snapshot.lastSuccessfulSync).toLocaleString() : 'Never'}</p>
          <p>{snapshot.isCurrent ? 'Snapshot matches saved settings' : 'No successful snapshot for the current settings (or list disabled)'}</p>
          <p>{snapshot.items.length} list items</p>
          <ul>
            {snapshot.items.slice(0, visible).map((item) => (
              <li key={JSON.stringify([item.sourceBookId, item.sourceEditionId, item.sourceAuthorId, item.sourceTitle, item.sourceAuthor])}>
                <div>{item.sourceTitle || item.sourceAuthor || 'Unnamed list item'}</div>
                {item.sourceTitle && item.sourceAuthor && <div>{item.sourceAuthor}</div>}
                {item.sourceBookId && <div>Source book: {item.sourceBookId}</div>}
                {item.sourceEditionId && <div>Source edition: {item.sourceEditionId}</div>}
                {item.sourceAuthorId && <div>Source author: {item.sourceAuthorId}</div>}
                {item.bookId && item.bookId !== item.sourceBookId && <div>Recording: {item.bookId}</div>}
                {item.editionId && item.editionId !== item.sourceEditionId && <div>Edition: {item.editionId}</div>}
                {item.authorId && item.authorId !== item.sourceAuthorId && <div>Author: {item.authorId}</div>}
                <div>{membershipStatus(item)}</div>
              </li>
            ))}
          </ul>
          {snapshot.items.length > visible && <Button onPress={() => setVisible(visible + 50)}>Show more list items</Button>}
        </>
      }
    </details>
  );
}

ImportListMembership.propTypes = { id: PropTypes.number.isRequired };

export default ImportListMembership;
