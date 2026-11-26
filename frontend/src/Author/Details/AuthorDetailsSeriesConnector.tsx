/* eslint max-params: 0 */
import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { BookId } from 'Book/BookIdentity';
import { toggleBooksMonitored } from 'Store/Actions/bookActions';
import { setSeriesSort, setSeriesTableOption } from 'Store/Actions/seriesActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import { asStateRecord, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import AuthorDetailsSeries, { type AuthorDetailsSeriesProps } from './AuthorDetailsSeries';
import { detailBookRows, detailColumns, type PositionMap,seriesRows, sortDirection, sortSeriesBooks } from './detailRowsState';

interface OwnProps extends Pick<AuthorDetailsSeriesProps, 'authorId' | 'isExpanded' | 'onExpandPress'> {
  seriesId: number;
}

function createMapStateToProps() {
  return createSelector(
    (_state: LegacyRootState, { seriesId }: OwnProps) => seriesId,
    (_state: LegacyRootState, { authorId }: OwnProps) => authorId,
    (state: LegacyRootState) => asStateRecord(state.books),
    createAuthorSelector(),
    (state: LegacyRootState) => asStateRecord(state.series),
    createDimensionsSelector(),
    (seriesId, authorId, books, author, series, dimensions) => {
      const currentSeries = seriesRows(series.items).find((item) => item.id === seriesId);
      const positionMap: PositionMap = {};
      currentSeries?.links.forEach((link) => { positionMap[link.bookId] = link.position; });
      const bookIds = new Set(currentSeries?.links.map((link) => link.bookId) ?? []);
      const booksInSeries = detailBookRows(books.items, authorId).filter((book) => bookIds.has(book.id));
      const sortKey = typeof series.sortKey === 'string' ? series.sortKey : undefined;
      const direction = sortDirection(series.sortDirection);
      return {
        id: seriesId,
        label: currentSeries?.title ?? '',
        items: sortSeriesBooks(booksInSeries, positionMap, sortKey, direction),
        positionMap,
        columns: detailColumns(series.columns),
        sortKey,
        sortDirection: direction,
        authorMonitored: author?.monitored === true,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const connector = connect(createMapStateToProps, { toggleBooksMonitored, setSeriesTableOption, dispatchSetSeriesSort: setSeriesSort });
type Props = OwnProps & ConnectedProps<typeof connector>;

class AuthorDetailsSeriesConnector extends Component<Props> {
  onTableOptionChange = (payload: StateRecord) => { this.props.setSeriesTableOption(payload); };
  onSortPress = (sortKey: string) => { this.props.dispatchSetSeriesSort({ sortKey }); };
  onMonitorBookPress = (bookIds: BookId[], monitored: boolean) => { this.props.toggleBooksMonitored({ bookIds, monitored }); };

  render() {
    return <AuthorDetailsSeries {...this.props} onSortPress={this.onSortPress} onTableOptionChange={this.onTableOptionChange} onMonitorBookPress={this.onMonitorBookPress} />;
  }
}

export default connector(AuthorDetailsSeriesConnector);
