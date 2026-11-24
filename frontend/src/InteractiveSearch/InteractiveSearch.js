import classNames from 'classnames';
import PropTypes from 'prop-types';
import React, { useLayoutEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TableHeader from 'Components/Table/TableHeader';
import TableHeaderCell from 'Components/Table/TableHeaderCell';
import { icons, kinds, sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InteractiveSearchRow from './InteractiveSearchRow';
import { releaseIdentity } from './releaseWindow';
import { useReleaseWindow, useSearchWidth } from './useReleaseWindow';
import styles from './InteractiveSearch.css';

const columns = [
  {
    name: 'protocol',
    label: 'Source',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'age',
    label: 'Age',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'title',
    label: 'Title',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'indexer',
    label: 'Indexer',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'size',
    label: 'Size',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'peers',
    label: 'Peers',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'qualityWeight',
    label: 'Quality',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'customFormatScore',
    label: React.createElement(Icon, {
      name: icons.SCORE,
      title: () => translate('CustomFormatScore')
    }),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'indexerFlags',
    label: React.createElement(Icon, {
      name: icons.FLAG,
      title: () => translate('IndexerFlags')
    }),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'rejections',
    label: React.createElement(Icon, {
      name: icons.DANGER,
      title: 'Rejections'
    }),
    isSortable: true,
    fixedSortDirection: sortDirections.ASCENDING,
    isVisible: true
  },
  {
    name: 'releaseWeight',
    label: React.createElement(Icon, { name: icons.DOWNLOAD }),
    isSortable: true,
    fixedSortDirection: sortDirections.ASCENDING,
    isVisible: true
  }
];

function InteractiveSearch(props) {
  const {
    searchPayload,
    isFetching,
    isPopulated,
    error,
    totalReleasesCount,
    items,
    sortKey,
    sortDirection,
    longDateFormat,
    timeFormat,
    onSortPress,
    onGrabPress,
    textFilter = '',
    onTextFilterChange,
    onRefreshPress,
    filterMenu,
    canGrab = true,
    needsRefresh = false,
    searchScopeKey
  } = props;

  const { ref: root, width, cards } = useSearchWidth();
  const keys = useMemo(() => items.map(releaseIdentity), [items]);
  const [pending, setPending] = useState(null);
  const confirmation = pending && canGrab && pending.scope === searchScopeKey && keys.includes(pending.key) ? pending : null;
  const resultWindow = useReleaseWindow(keys, width, cards, searchScopeKey, confirmation?.key);

  useLayoutEffect(() => {
    if (pending && !confirmation) {
      setPending(null);
    }
  }, [confirmation, pending]);

  const sortLabels = {
    protocol: translate('Source'),
    age: 'Age',
    title: translate('Title'),
    indexer: translate('Indexer'),
    size: translate('Size').trim(),
    peers: 'Peers',
    qualityWeight: translate('Quality'),
    customFormatScore: translate('CustomFormatScore'),
    indexerFlags: translate('IndexerFlags'),
    rejections: 'Rejections',
    releaseWeight: 'Release rank'
  };
  const currentSort = columns.find((column) => column.name === sortKey);

  function grab(item) {
    if (!canGrab) {
      return;
    }

    const payload = { ...searchPayload, guid: item.guid, indexerId: item.indexerId, searchResultId: item.searchResultId };
    if (item.downloadAllowed) {
      onGrabPress(payload);
    } else {
      setPending({ key: releaseIdentity(item), scope: searchScopeKey, title: item.title, payload });
    }
  }

  const row = (index) => (
    <InteractiveSearchRow
      key={keys[index]}
      {...items[index]}
      rowKey={keys[index]}
      index={index}
      total={items.length}
      card={cards}
      canGrab={canGrab}
      longDateFormat={longDateFormat}
      timeFormat={timeFormat}
      onGrabPress={() => grab(items[index])}
    />
  );

  const results = resultWindow.entries.map((entry) => {
    if ('index' in entry) {
      return row(entry.index);
    }

    const height = entry.gap;
    return cards ?
      <div key={`gap-${entry.start}`} aria-hidden={true} style={{ height }} /> :
      <tr key={`gap-${entry.start}`} aria-hidden={true}><td className={styles.spacer} colSpan={columns.length} style={{ height }} /></tr>;
  });

  return (
    <div ref={root} className={styles.search} role="region" aria-label={translate('InteractiveSearchModalHeader')}>
      <div className={styles.searchControls}>
        {
          onTextFilterChange &&
            <TextInput
              name="releaseTextFilter"
              type="search"
              aria-label={translate('InteractiveSearchFilterText')}
              placeholder={translate('InteractiveSearchFilterText')}
              value={textFilter}
              onChange={({ value }) => onTextFilterChange(value)}
            />
        }
        {filterMenu}
        {onRefreshPress && <Button isDisabled={isFetching} onPress={onRefreshPress}>{translate('Refresh')}</Button>}
      </div>
      {
        isPopulated &&
          <div className={styles.resultCount} role="status">
            {translate('InteractiveSearchResultCount', [items.length, totalReleasesCount])}
          </div>
      }
      {needsRefresh && <Alert kind={kinds.WARNING}>{translate('InteractiveSearchRefreshRequired')}</Alert>}
      {
        isFetching ? <LoadingIndicator /> : null
      }

      {
        !isFetching && error ?
          <div className={styles.blankpad}>
            Unable to load results for this book search. Try again later
          </div> :
          null
      }

      {
        !isFetching && isPopulated && !totalReleasesCount ?
          <Alert kind={kinds.INFO}>
            {translate('NoResultsFound')}
          </Alert> :
          null
      }

      {
        !!totalReleasesCount && isPopulated && !items.length ?
          <Alert kind={kinds.WARNING}>
            {translate('AllResultsAreHiddenByTheAppliedFilter')}
          </Alert> :
          null
      }

      {
        cards && isPopulated && !!items.length &&
          <div className={styles.cardSort}>
            <SelectInput
              name="releaseSortKey"
              aria-label={translate('InteractiveSearchSortBy')}
              value={sortKey}
              values={columns.map((column) => ({ key: column.name, value: sortLabels[column.name] }))}
              onChange={({ value }) => onSortPress(value, columns.find((column) => column.name === value)?.fixedSortDirection)}
            />
            <SelectInput
              name="releaseSortDirection"
              aria-label={translate('InteractiveSearchSortDirection')}
              value={sortDirection}
              isDisabled={!!currentSort?.fixedSortDirection}
              values={[
                { key: sortDirections.ASCENDING, value: translate('InteractiveSearchSortAscending') },
                { key: sortDirections.DESCENDING, value: translate('InteractiveSearchSortDescending') }
              ]}
              onChange={({ value }) => onSortPress(sortKey, value)}
            />
          </div>
      }
      <div
        ref={resultWindow.ref}
        role="region"
        aria-label={translate('InteractiveSearchResults')}
        aria-busy={isFetching}
        tabIndex={resultWindow.virtualized ? 0 : undefined}
        className={classNames(styles.resultViewport, resultWindow.virtualized && styles.virtualized)}
        onFocusCapture={resultWindow.onFocusCapture}
        onBlurCapture={resultWindow.onBlurCapture}
      >
        {
          isPopulated && !!items.length && (
            cards ?
              <div role="list" data-release-list={true}>{results}</div> :
              <table className={styles.resultsTable} aria-rowcount={items.length + 1}>
                <TableHeader>
                  {columns.map((column) => (
                    <TableHeaderCell key={column.name} {...column} sortKey={sortKey} sortDirection={sortDirection} onSortPress={onSortPress}>
                      {typeof column.label === 'function' ? column.label() : column.label}
                    </TableHeaderCell>
                  ))}
                </TableHeader>
                <tbody data-release-list={true}>{results}</tbody>
              </table>
          )
        }
      </div>
      <ConfirmModal
        isOpen={!!confirmation}
        kind={kinds.WARNING}
        title={translate('GrabRelease')}
        message={translate('GrabReleaseMessageText', [confirmation?.title ?? ''])}
        confirmLabel={translate('Grab')}
        onConfirm={() => {
          if (confirmation) {
            onGrabPress(confirmation.payload);
          }
          setPending(null);
        }}
        onCancel={() => setPending(null)}
      />
    </div>
  );
}

InteractiveSearch.propTypes = {
  searchPayload: PropTypes.object.isRequired,
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  totalReleasesCount: PropTypes.number.isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  sortKey: PropTypes.string,
  sortDirection: PropTypes.string,
  type: PropTypes.string.isRequired,
  longDateFormat: PropTypes.string.isRequired,
  timeFormat: PropTypes.string.isRequired,
  onSortPress: PropTypes.func.isRequired,
  onGrabPress: PropTypes.func.isRequired,
  textFilter: PropTypes.string,
  onTextFilterChange: PropTypes.func,
  onRefreshPress: PropTypes.func,
  filterMenu: PropTypes.node,
  canGrab: PropTypes.bool,
  needsRefresh: PropTypes.bool,
  searchScopeKey: PropTypes.string
};

export default InteractiveSearch;
