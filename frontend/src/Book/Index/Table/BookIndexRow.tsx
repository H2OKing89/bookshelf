import classNames from 'classnames';
import React, { Component } from 'react';
import AuthorNameLink from 'Author/AuthorNameLink';
import DeleteAuthorModal from 'Author/Delete/DeleteAuthorModal';
import EditAuthorModalConnector from 'Author/Edit/EditAuthorModalConnector';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import BookNameLink from 'Book/BookNameLink';
import EditBookModalConnector from 'Book/Edit/EditBookModalConnector';
import type { AuthorReference, LibraryColumn, LibraryImage, LibraryStatistics, NamedProfile, SelectionChange } from 'Book/LibraryPresentation';
import HeartRating from 'Components/HeartRating';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import VirtualTableSelectCell from 'Components/Table/Cells/VirtualTableSelectCell';
import TagListConnector from 'Components/TagListConnector';
import { icons } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import { formatRecordingQualityRank } from 'Utilities/Quality/qualityRank';
import translate from 'Utilities/String/translate';
import BookStatusCell from './BookStatusCell';
import styles from './BookIndexRow.css';

interface BookIndexRowProps {
  id: BookId;
  authorId: AuthorId;
  author: AuthorReference;
  title: string;
  displayTitle?: string | null;
  monitored: boolean;
  titleSlug: string;
  nextAiring?: string | null;
  statistics: LibraryStatistics;
  images: readonly LibraryImage[];
  qualityProfile?: NamedProfile | null;
  qualityProfileId?: number | null;
  releaseDate?: string | null;
  added?: string | null;
  sortKey: string;
  showSearchAction: boolean;
  isRefreshingBook: boolean;
  isSearchingBook: boolean;
  onRefreshBookPress: () => void;
  onSearchPress: () => void;
  isEditorActive: boolean;
  isSelected?: boolean;
  onSelectedChange: (change: SelectionChange<BookId>) => void;
  columns: readonly LibraryColumn[];
  genres: readonly string[];
  tags: number[];
  ratings: { value: number };
  runtimeLengthMinutes?: number | null;
  narrators?: readonly string[];
  metadataLabels?: readonly string[];
}

interface BookIndexRowState {
  isEditAuthorModalOpen: boolean;
  isDeleteAuthorModalOpen: boolean;
  hasBannerError: boolean;
  isEditBookModalOpen: boolean;
}

class BookIndexRow extends Component<BookIndexRowProps, BookIndexRowState> {
  static defaultProps = {
    statistics: {
      bookCount: 0,
      bookFileCount: 0,
      totalBookCount: 0
    },
    genres: [],
    tags: []
  };

  //
  // Lifecycle

  constructor(props: BookIndexRowProps) {
    super(props);

    this.state = {
      hasBannerError: false,
      isEditAuthorModalOpen: false,
      isDeleteAuthorModalOpen: false,
      isEditBookModalOpen: false
    };
  }

  onEditAuthorPress = () => {
    this.setState({ isEditAuthorModalOpen: true });
  };

  onEditAuthorModalClose = () => {
    this.setState({ isEditAuthorModalOpen: false });
  };

  onDeleteAuthorPress = () => {
    this.setState({
      isEditAuthorModalOpen: false,
      isDeleteAuthorModalOpen: true
    });
  };

  onDeleteAuthorModalClose = () => {
    this.setState({ isDeleteAuthorModalOpen: false });
  };

  onEditBookPress = () => {
    this.setState({ isEditBookModalOpen: true });
  };

  onEditBookModalClose = () => {
    this.setState({ isEditBookModalOpen: false });
  };

  onUseSceneNumberingChange = () => {
    // Mock handler to satisfy `onChange` being required for `CheckInput`.
    //
  };

  onBannerLoad = () => {
    if (this.state.hasBannerError) {
      this.setState({ hasBannerError: false });
    }
  };

  onBannerLoadError = () => {
    if (!this.state.hasBannerError) {
      this.setState({ hasBannerError: true });
    }
  };

  //
  // Render

  render() {
    const {
      id,
      authorId,
      monitored,
      title,
      displayTitle,
      author,
      titleSlug,
      qualityProfile,
      releaseDate,
      runtimeLengthMinutes,
      narrators = [],
      added,
      statistics,
      genres,
      metadataLabels = [],
      ratings,
      tags,
      showSearchAction,
      columns,
      isRefreshingBook,
      isSearchingBook,
      isEditorActive,
      isSelected,
      onRefreshBookPress,
      onSearchPress,
      onSelectedChange
    } = this.props;

    const {
      bookFileCount = 0,
      sizeOnDisk = 0
    } = statistics;

    const {
      isEditAuthorModalOpen,
      isDeleteAuthorModalOpen,
      isEditBookModalOpen
    } = this.state;

    return (
      <>
        {
          columns.map((column) => {
            const {
              name,
              isVisible
            } = column;

            if (!isVisible) {
              return null;
            }

            if (isEditorActive && name === 'select') {
              return (
                <VirtualTableSelectCell
                  inputClassName={styles.checkInput}
                  id={id}
                  key={name}
                  isSelected={isSelected}
                  isDisabled={false}
                  onSelectedChange={onSelectedChange}
                />
              );
            }

            if (name === 'status') {
              return (
                <BookStatusCell
                  key={name}
                  className={styles[name]}
                  monitored={monitored}
                  component={VirtualTableRowCell}
                />
              );
            }

            if (name === 'title') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={classNames(
                    styles[name]
                  )}
                >
                  <BookNameLink
                    titleSlug={titleSlug}
                    title={title}
                    displayTitle={displayTitle}
                  />
                </VirtualTableRowCell>
              );
            }

            if (name === 'authorName') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={classNames(
                    styles[name]
                  )}
                >
                  <AuthorNameLink
                    titleSlug={author.titleSlug}
                    authorName={author.authorName}
                  />
                </VirtualTableRowCell>
              );
            }

            if (name === 'runtimeLengthMinutes') {
              return (
                <VirtualTableRowCell key={name} className={styles.runtimeLengthMinutes}>
                  {runtimeLengthMinutes != null && runtimeLengthMinutes > 0 ? translate('RuntimeMinutes', { 0: runtimeLengthMinutes }) : ''}
                </VirtualTableRowCell>
              );
            }

            if (name === 'narrators') {
              const narratorNames = narrators.join(', ');

              return (
                <VirtualTableRowCell key={name} className={styles.narrators}>
                  <span title={narratorNames}>{narratorNames}</span>
                </VirtualTableRowCell>
              );
            }

            if (name === 'fileQualityRank') {
              return (
                <VirtualTableRowCell key={name} className={styles.fileQualityRank}>
                  <span title="Lowest ranked file in the monitored edition (relative to its quality profile)">
                    {formatRecordingQualityRank(statistics.fileQualityRank)}
                  </span>
                </VirtualTableRowCell>
              );
            }

            if (name === 'qualityProfileId') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {qualityProfile?.name ?? ''}
                </VirtualTableRowCell>
              );
            }

            if (name === 'releaseDate') {
              return (
                <RelativeDateCellConnector
                  key={name}
                  className={styles[name]}
                  date={releaseDate}
                  component={VirtualTableRowCell}
                />
              );
            }

            if (name === 'added') {
              return (
                <RelativeDateCellConnector
                  key={name}
                  className={styles[name]}
                  date={added}
                  component={VirtualTableRowCell}
                />
              );
            }

            if (name === 'bookFileCount') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {bookFileCount}
                </VirtualTableRowCell>
              );
            }

            if (name === 'path') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {author.path}
                </VirtualTableRowCell>
              );
            }

            if (name === 'sizeOnDisk') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {formatBytes(sizeOnDisk)}
                </VirtualTableRowCell>
              );
            }

            if (name === 'genres') {
              const joinedGenres = genres.join(', ');

              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <span title={joinedGenres}>
                    {joinedGenres}
                  </span>
                </VirtualTableRowCell>
              );
            }

            if (name === 'metadataLabels') {
              const joinedLabels = metadataLabels.join(', ');

              return (
                <VirtualTableRowCell key={name} className={styles.genres}>
                  <span title={joinedLabels}>{joinedLabels}</span>
                </VirtualTableRowCell>
              );
            }

            if (name === 'ratings') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <HeartRating
                    rating={ratings.value}
                  />
                </VirtualTableRowCell>
              );
            }

            if (name === 'tags') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <TagListConnector
                    tags={tags}
                  />
                </VirtualTableRowCell>
              );
            }

            if (name === 'actions') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <SpinnerIconButton
                    name={icons.REFRESH}
                    title={translate('RefreshBook')}
                    isSpinning={isRefreshingBook}
                    onPress={onRefreshBookPress}
                  />

                  {
                    showSearchAction &&
                      <SpinnerIconButton
                        name={icons.SEARCH}
                        title={translate('SearchForMonitoredBooks')}
                        isSpinning={isSearchingBook}
                        onPress={onSearchPress}
                      />
                  }

                  <IconButton
                    name={icons.INTERACTIVE}
                    title={translate('EditAuthor')}
                    onPress={this.onEditAuthorPress}
                  />

                  <IconButton
                    name={icons.EDIT}
                    title={translate('EditBook')}
                    onPress={this.onEditBookPress}
                  />
                </VirtualTableRowCell>
              );
            }

            return null;
          })
        }

        <EditAuthorModalConnector
          isOpen={isEditAuthorModalOpen}
          authorId={authorId}
          onModalClose={this.onEditAuthorModalClose}
          onDeleteAuthorPress={this.onDeleteAuthorPress}
        />

        <DeleteAuthorModal
          isOpen={isDeleteAuthorModalOpen}
          authorId={authorId}
          onModalClose={this.onDeleteAuthorModalClose}
        />

        <EditBookModalConnector
          isOpen={isEditBookModalOpen}
          authorId={authorId}
          bookId={id}
          onModalClose={this.onEditBookModalClose}
        />
      </>
    );
  }
}

export default BookIndexRow;
