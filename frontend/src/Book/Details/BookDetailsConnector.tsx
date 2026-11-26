/* eslint max-params: 0 */
import _ from 'lodash';
import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { parseResourceId } from 'Book/BookIdentity';
import * as commandNames from 'Commands/commandNames';
import { invalidateReleaseSearches } from 'InteractiveSearch/releaseSearchQuery';
import { updateItem } from 'Store/Actions/baseActions';
import { clearBookFiles, fetchBookFiles } from 'Store/Actions/bookFileActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { clearEditions, fetchEditions } from 'Store/Actions/editionActions';
import createAllAuthorsSelector from 'Store/Selectors/createAllAuthorsSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import { findCommand, isCommandExecuting } from 'Utilities/Command';
import { registerPagePopulator, unregisterPagePopulator } from 'Utilities/pagePopulator';
import BookDetails, { type BookDetailsProps, type BookNavigationEntry } from './BookDetails';
import { authorReference, detailStatistics, optionalText } from './detailPresentation';

interface OwnProps { titleSlug: string }

function navigationBook(value: unknown): BookNavigationEntry {
  const record = asStateRecord(value);
  return { id: parseResourceId(record.id, 'book'), title: optionalText(record.title) ?? '', titleSlug: optionalText(record.titleSlug) ?? '' };
}

export function monitoredEditionIds(value: unknown) {
  return Array.isArray(value) ? value.map(asStateRecord).filter((edition) => edition.monitored === true)
    .map((edition) => edition.id).filter((id): id is number => typeof id === 'number').sort((a, b) => a - b) : [];
}

function createMapStateToProps() {
  return createSelector(
    (_state: LegacyRootState, { titleSlug }: OwnProps) => titleSlug,
    (state: LegacyRootState) => state.bookFiles,
    (state: LegacyRootState) => state.books,
    (state: LegacyRootState) => state.editions,
    createAllAuthorsSelector(),
    createCommandsSelector(),
    (titleSlug, bookFiles, books, editions, authors, commands) => {
      const records = Array.isArray(books.items) ? books.items.map(asStateRecord) : [];
      const book = records.find((b) => b.titleSlug === titleSlug);
      const author = authors.find((a) => a.id === book?.authorId);
      if (!book || !author) {
        return { detail: null };
      }
      const id = parseResourceId(book.id, 'book');
      const sortedBooks = records.filter((b) => b.authorId === book.authorId).sort((a, b) =>
        (optionalText(a.releaseDate) ?? '').localeCompare(optionalText(b.releaseDate) ?? ''));
      const bookIndex = sortedBooks.findIndex((b) => b.id === id);
      const previousBook = navigationBook(sortedBooks[bookIndex - 1] ?? sortedBooks[sortedBooks.length - 1]);
      const nextBook = navigationBook(sortedBooks[bookIndex + 1] ?? sortedBooks[0]);
      const refreshing = findCommand(commands, { name: commandNames.REFRESH_BOOK });
      const isRefreshing = isCommandExecuting(refreshing) && asStateRecord(refreshing?.body).bookId === id;
      const isRefreshingMediaInfo = commands.some((command) => {
        const body = asStateRecord(command.body);
        return isCommandExecuting(command) && typeof body.name === 'string' &&
          body.name.toLowerCase() === commandNames.REFRESH_MEDIA_INFO.toLowerCase() &&
          (!body.bookId || body.bookId === id) && (!body.authorId || body.authorId === author.id);
      });
      const search = findCommand(commands, { name: commandNames.BOOK_SEARCH });
      const bookIds = asStateRecord(search?.body).bookIds;
      const rename = findCommand(commands, { name: commandNames.RENAME_AUTHOR });
      return {
        detail: {
          id, title: optionalText(book.title) ?? '', foreignBookId: optionalText(book.foreignBookId),
          author: { ...author, ...authorReference(author) },
          statistics: detailStatistics(book.statistics),
          previousBook, nextBook,
          anyReleaseOk: book.anyReleaseOk,
          monitoredEditionIds: monitoredEditionIds(book.editions),
          isRefreshing, isRefreshingMediaInfo,
          isSearching: isCommandExecuting(search) && Array.isArray(bookIds) && bookIds.includes(id),
          isRenamingFiles: isCommandExecuting(findCommand(commands, { name: commandNames.RENAME_FILES, authorId: author.id })),
          isRenamingAuthor: isCommandExecuting(rename) && rename?.body.authorIds?.includes(author.id) === true,
          isFetching: bookFiles.isFetching === true || editions.isFetching === true,
          isPopulated: bookFiles.isPopulated === true && editions.isPopulated === true,
          bookFilesError: bookFiles.error,
          hasBookFiles: Array.isArray(bookFiles.items) && bookFiles.items.length > 0
        }
      };
    }
  );
}

const connector = connect(createMapStateToProps, {
  executeCommand, fetchBookFiles, clearBookFiles, fetchEditions, clearEditions, updateItem
});
type Props = OwnProps & ConnectedProps<typeof connector>;

class BookDetailsConnector extends Component<Props> {
  componentDidMount() {
    registerPagePopulator(this.populate);
    this.populate();
  }

  componentDidUpdate(prevProps: Props) {
    const current = this.props.detail;
    const previous = prevProps.detail;
    if (current && previous?.id === current.id && (
      (previous.isRenamingFiles && !current.isRenamingFiles) ||
      (previous.isRenamingAuthor && !current.isRenamingAuthor) ||
      (previous.isRefreshingMediaInfo && !current.isRefreshingMediaInfo) ||
      !_.isEqual(previous.monitoredEditionIds, current.monitoredEditionIds) ||
      (previous.anyReleaseOk === false && current.anyReleaseOk === true)
    )) {
      invalidateReleaseSearches({ bookId: current.id });
      this.unpopulate();
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
    const bookId = this.props.detail?.id;
    if (bookId) {
      this.props.fetchBookFiles({ bookId });
      this.props.fetchEditions({ bookId });
    }
  };

  unpopulate = () => {
    this.props.clearBookFiles();
    this.props.clearEditions();
  };

  onLocalMetadataSaved = (value: unknown) => {
    const book = asStateRecord(value);
    const id = this.props.detail?.id;
    if (id && book.id === id) {
      this.props.updateItem({ ...book, section: 'books', id });
      this.props.fetchEditions({ bookId: id });
      invalidateReleaseSearches({ bookId: id });
    }
  };

  onRefreshPress = () => {
    const bookId = this.props.detail?.id;
    if (bookId) this.props.executeCommand({ name: commandNames.REFRESH_BOOK, bookId });
  };

  onRefreshMediaInfoPress = () => {
    const bookId = this.props.detail?.id;
    if (bookId) this.props.executeCommand({ name: commandNames.REFRESH_MEDIA_INFO, bookId });
  };

  onSearchPress = () => {
    const bookId = this.props.detail?.id;
    if (bookId) this.props.executeCommand({ name: commandNames.BOOK_SEARCH, bookIds: [bookId] });
  };

  render() {
    const { detail } = this.props;
    if (!detail) return null;
    const viewProps: BookDetailsProps = {
      ...detail,
      onRefreshPress: this.onRefreshPress,
      onRefreshMediaInfoPress: this.onRefreshMediaInfoPress,
      onSearchPress: this.onSearchPress,
      onLocalMetadataSaved: this.onLocalMetadataSaved
    };
    return <BookDetails {...viewProps} />;
  }
}
export default connector(BookDetailsConnector);
