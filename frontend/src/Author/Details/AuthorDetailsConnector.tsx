/* eslint max-params: 0 */
import _ from 'lodash';
import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { type BookId } from 'Book/BookIdentity';
import { authorHeaderData,authorReference } from 'Book/Details/detailPresentation';
import * as commandNames from 'Commands/commandNames';
import { invalidateReleaseSearches } from 'InteractiveSearch/releaseSearchQuery';
import { clearBookFiles, fetchBookFiles } from 'Store/Actions/bookFileActions';
import { saveBookEditor } from 'Store/Actions/bookIndexActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { clearQueueDetails, fetchQueueDetails } from 'Store/Actions/queueActions';
import { clearSeries, fetchSeries } from 'Store/Actions/seriesActions';
import createAllAuthorsSelector from 'Store/Selectors/createAllAuthorsSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import { asStateRecord, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { findCommand, isCommandExecuting } from 'Utilities/Command';
import { registerPagePopulator, unregisterPagePopulator } from 'Utilities/pagePopulator';
import AuthorDetails, { type AuthorDetailsProps } from './AuthorDetails';
import { seriesRows } from './detailRowsState';

interface OwnProps { titleSlug: string; onReconcileAuthorPress?: () => void }

function createMapStateToProps() {
  return createSelector(
    (_state: LegacyRootState, { titleSlug }: OwnProps) => titleSlug,
    (state: LegacyRootState) => state.books,
    (state: LegacyRootState) => state.bookIndex,
    (state: LegacyRootState) => state.series,
    (state: LegacyRootState) => state.bookFiles,
    createAllAuthorsSelector(),
    createCommandsSelector(),
    (titleSlug, books, index, series, bookFiles, allAuthors, commands) => {
      const sortedAuthors = _.orderBy(allAuthors, 'sortNameLastFirst');
      const authorIndex = sortedAuthors.findIndex((author) => author.titleSlug === titleSlug);
      const author = sortedAuthors[authorIndex];
      if (!author) {
        return { detail: null };
      }
      const previous = sortedAuthors[authorIndex - 1] ?? sortedAuthors[sortedAuthors.length - 1];
      const next = sortedAuthors[authorIndex + 1] ?? sortedAuthors[0];
      const isAuthorRefreshing = isCommandExecuting(findCommand(commands, { name: commandNames.REFRESH_AUTHOR, authorId: author.id }));
      const refresh = findCommand(commands, { name: commandNames.REFRESH_AUTHOR });
      const allAuthorRefreshing = isCommandExecuting(refresh) && !refresh?.body.authorId;
      const rename = findCommand(commands, { name: commandNames.RENAME_AUTHOR });
      const bookRows = Array.isArray(books.items) ? books.items.map(asStateRecord) : [];
      const seriesItems = seriesRows(series.items);
      return {
        detail: {
          ...authorHeaderData(author), id: author.id,
          previousAuthor: { id: previous.id, ...authorReference(previous) },
          nextAuthor: { id: next.id, ...authorReference(next) },
          isAuthorRefreshing, allAuthorRefreshing,
          isRefreshing: isAuthorRefreshing || allAuthorRefreshing,
          isSearching: isCommandExecuting(findCommand(commands, { name: commandNames.AUTHOR_SEARCH, authorId: author.id })),
          isRenamingFiles: isCommandExecuting(findCommand(commands, { name: commandNames.RENAME_FILES, authorId: author.id })),
          isRenamingAuthor: isCommandExecuting(rename) && rename?.body.authorIds?.includes(author.id) === true,
          isFetching: books.isFetching === true || series.isFetching === true || bookFiles.isFetching === true,
          isPopulated: books.isPopulated === true && series.isPopulated === true && bookFiles.isPopulated === true,
          booksError: books.error, bookFilesError: bookFiles.error,
          isSaving: index.isSaving === true, saveError: index.saveError,
          isDeleting: index.isDeleting === true, deleteError: index.deleteError,
          hasBooks: bookRows.length > 0,
          hasMonitoredBooks: bookRows.some((book) => book.monitored === true),
          hasSeries: seriesItems.length > 0, series: seriesItems,
          hasBookFiles: Array.isArray(bookFiles.items) && bookFiles.items.length > 0
        }
      };
    }
  );
}

const connector = connect(createMapStateToProps, {
  fetchSeries, clearSeries, saveBookEditor, fetchBookFiles, clearBookFiles,
  fetchQueueDetails, clearQueueDetails, executeCommand
});
type Props = OwnProps & ConnectedProps<typeof connector>;

class AuthorDetailsConnector extends Component<Props> {
  componentDidMount() {
    registerPagePopulator(this.populate);
    this.populate();
  }

  componentDidUpdate(prevProps: Props) {
    const current = this.props.detail;
    const previous = prevProps.detail;
    if (current && previous?.id === current.id && (
      (previous.isAuthorRefreshing && !current.isAuthorRefreshing) ||
      (previous.allAuthorRefreshing && !current.allAuthorRefreshing) ||
      (previous.isRenamingFiles && !current.isRenamingFiles) ||
      (previous.isRenamingAuthor && !current.isRenamingAuthor)
    )) {
      invalidateReleaseSearches({ authorId: current.id });
      this.populate();
    }
    if (previous?.id !== current?.id) {
      this.unpopulate();
      this.populate();
    }
  }

  componentWillUnmount() {
    unregisterPagePopulator(this.populate);
    this.unpopulate();
  }

  populate = () => {
    const authorId = this.props.detail?.id;
    if (authorId) {
      this.props.fetchSeries({ authorId });
      this.props.fetchBookFiles({ authorId });
      this.props.fetchQueueDetails({ authorId });
    }
  };

  unpopulate = () => {
    this.props.clearSeries();
    this.props.clearBookFiles();
    this.props.clearQueueDetails();
  };

  onRefreshPress = () => {
    const authorId = this.props.detail?.id;
    if (authorId) this.props.executeCommand({ name: commandNames.REFRESH_AUTHOR, authorId });
  };

  onSearchPress = () => {
    const authorId = this.props.detail?.id;
    if (authorId) this.props.executeCommand({ name: commandNames.AUTHOR_SEARCH, authorId });
  };

  onSaveSelected = (payload: StateRecord & { bookIds: BookId[] }) => {
    this.props.saveBookEditor(payload);
  };

  render() {
    const { detail, onReconcileAuthorPress } = this.props;
    if (!detail) return null;
    const viewProps: AuthorDetailsProps = {
      ...detail, onReconcileAuthorPress,
      onRefreshPress: this.onRefreshPress, onSearchPress: this.onSearchPress,
      onSaveSelected: this.onSaveSelected
    };
    return <AuthorDetails {...viewProps} />;
  }
}
export default connector(AuthorDetailsConnector);
