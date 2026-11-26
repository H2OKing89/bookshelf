import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import { setAuthorDetailsId, setAuthorDetailsSort } from 'Store/Actions/authorDetailsActions';
import { setBooksTableOption, toggleBooksMonitored } from 'Store/Actions/bookActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import type { StateRecord } from 'Store/StateTypes';
import AuthorDetailsSeason, { type AuthorDetailsSeasonProps } from './AuthorDetailsSeason';
import { detailBookRows, detailColumns, sortDirection, sortSeriesBooks } from './detailRowsState';

interface OwnProps extends Pick<AuthorDetailsSeasonProps, 'selectedState' | 'setSelectedState' | 'onSelectedChange' | 'isEditorActive'> {
  authorId: AuthorId;
}

function createMapStateToProps() {
  return createSelector(
    createClientSideCollectionSelector('books', 'authorDetails'),
    (_state: unknown, { authorId }: OwnProps) => authorId,
    (books, authorId) => {
      const sortKey = typeof books.sortKey === 'string' ? books.sortKey : undefined;
      const direction = sortDirection(books.sortDirection);
      return {
        items: sortSeriesBooks(detailBookRows(books.items, authorId), {}, sortKey, direction),
        columns: detailColumns(books.columns),
        sortKey,
        sortDirection: direction
      };
    }
  );
}

const connector = connect(createMapStateToProps, { setAuthorDetailsId, setAuthorDetailsSort, toggleBooksMonitored, setBooksTableOption });
type Props = OwnProps & ConnectedProps<typeof connector>;

class AuthorDetailsSeasonConnector extends Component<Props> {
  componentDidMount() {
    this.props.setAuthorDetailsId({ authorId: this.props.authorId });
  }

  componentDidUpdate(previous: Props) {
    if (previous.authorId !== this.props.authorId) {
      this.props.setAuthorDetailsId({ authorId: this.props.authorId });
    }
  }

  onTableOptionChange = (payload: StateRecord) => { this.props.setBooksTableOption(payload); };
  onSortPress = (sortKey: string) => { this.props.setAuthorDetailsSort({ sortKey }); };
  onMonitorBookPress = (bookIds: BookId[], monitored: boolean) => { this.props.toggleBooksMonitored({ bookIds, monitored }); };

  render() {
    return <AuthorDetailsSeason {...this.props} onSortPress={this.onSortPress} onTableOptionChange={this.onTableOptionChange} onMonitorBookPress={this.onMonitorBookPress} />;
  }
}

export default connector(AuthorDetailsSeasonConnector);
