import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import { authorHistoryMarkAsFailed, clearAuthorHistory, fetchAuthorHistory } from 'Store/Actions/authorHistoryActions';
import type { LegacyRootState } from 'Store/StateTypes';
import AuthorHistoryModalContent from './AuthorHistoryModalContent';
import { projectAuthorHistory } from './authorHistoryState';
import AuthorHistoryTableContent from './AuthorHistoryTableContent';

export interface AuthorHistoryScope {
  authorId: AuthorId;
  bookId?: BookId | null;
}

type OwnProps = AuthorHistoryScope & (
  { mode: 'table' } | { mode: 'modal'; onModalClose: () => void }
);

const mapState = createSelector(
  (state: LegacyRootState) => state.authorHistory,
  projectAuthorHistory
);
const connector = connect(mapState, {
  fetchAuthorHistory,
  clearAuthorHistory,
  authorHistoryMarkAsFailed
});
type Props = OwnProps & ConnectedProps<typeof connector>;

class AuthorHistoryContentConnector extends Component<Props> {
  componentDidMount() {
    this.fetchHistory();
  }

  componentDidUpdate(previous: Props) {
    if (previous.authorId !== this.props.authorId || previous.bookId !== this.props.bookId) {
      this.fetchHistory();
    }
  }

  componentWillUnmount() {
    this.props.clearAuthorHistory();
  }

  fetchHistory = () => {
    const { authorId, bookId } = this.props;
    this.props.fetchAuthorHistory({ authorId, bookId });
  };

  onMarkAsFailedPress = (historyId: number) => {
    const { authorId, bookId } = this.props;
    this.props.authorHistoryMarkAsFailed({ historyId, authorId, bookId });
  };

  render() {
    const { bookId, isFetching, isPopulated, error, items } = this.props;
    const props = { bookId, isFetching, isPopulated, error, items, onMarkAsFailedPress: this.onMarkAsFailedPress };
    return this.props.mode === 'modal' ?
      <AuthorHistoryModalContent {...props} onModalClose={this.props.onModalClose} /> :
      <AuthorHistoryTableContent {...props} />;
  }
}

export default connector(AuthorHistoryContentConnector);
