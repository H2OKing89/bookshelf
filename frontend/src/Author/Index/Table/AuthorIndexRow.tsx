import classNames from 'classnames';
import { Component } from 'react';
import type { AuthorStatus } from 'Author/Author';
import AuthorBanner from 'Author/AuthorBanner';
import AuthorNameLink from 'Author/AuthorNameLink';
import DeleteAuthorModal from 'Author/Delete/DeleteAuthorModal';
import EditAuthorModalConnector from 'Author/Edit/EditAuthorModalConnector';
import type { AuthorId } from 'Book/BookIdentity';
import BookTitleLink from 'Book/BookTitleLink';
import type { BookReference, LibraryColumn, LibraryImage, LibraryStatistics, NamedProfile, SelectionChange } from 'Book/LibraryPresentation';
import HeartRating from 'Components/HeartRating';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import ProgressBar from 'Components/ProgressBar';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import VirtualTableSelectCell from 'Components/Table/Cells/VirtualTableSelectCell';
import TagListConnector from 'Components/TagListConnector';
import { icons } from 'Helpers/Props';
import getProgressBarKind from 'Utilities/Author/getProgressBarKind';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import AuthorStatusCell from './AuthorStatusCell';
import hasGrowableColumns from './hasGrowableColumns';
import styles from './AuthorIndexRow.css';

interface AuthorIndexRowProps {
  id: AuthorId;
  authorName: string;
  authorNameLastFirst: string;
  monitored: boolean;
  status: AuthorStatus;
  titleSlug: string;
  nextAiring?: string | null;
  statistics: LibraryStatistics;
  images: readonly LibraryImage[];
  qualityProfile?: NamedProfile | null;
  qualityProfileId?: number | null;
  metadataProfile?: NamedProfile | null;
  nextBook?: BookReference | null;
  lastBook?: BookReference | null;
  added?: string | null;
  path: string;
  sortKey: string;
  showSearchAction: boolean;
  isRefreshingAuthor: boolean;
  isSearchingAuthor: boolean;
  onRefreshAuthorPress: () => void;
  onSearchPress: () => void;
  isEditorActive: boolean;
  isSelected?: boolean;
  onSelectedChange: (change: SelectionChange<AuthorId>) => void;
  columns: readonly LibraryColumn[];
  genres: readonly string[];
  tags: number[];
  ratings: { value: number };
  showBanners: boolean;
  showTitle: string;
}

interface AuthorIndexRowState {
  isEditAuthorModalOpen: boolean;
  isDeleteAuthorModalOpen: boolean;
  hasBannerError: boolean;
}

class AuthorIndexRow extends Component<AuthorIndexRowProps, AuthorIndexRowState> {
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

  constructor(props: AuthorIndexRowProps) {
    super(props);

    this.state = {
      hasBannerError: false,
      isEditAuthorModalOpen: false,
      isDeleteAuthorModalOpen: false
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
      monitored,
      status,
      authorName,
      authorNameLastFirst,
      titleSlug,
      qualityProfile,
      metadataProfile,
      nextBook,
      lastBook,
      added,
      statistics = {},
      genres,
      ratings,
      path,
      tags,
      images,
      showBanners,
      showTitle,
      showSearchAction,
      columns,
      isRefreshingAuthor,
      isSearchingAuthor,
      isEditorActive,
      isSelected,
      onRefreshAuthorPress,
      onSearchPress,
      onSelectedChange
    } = this.props;

    const {
      bookCount = 0,
      availableBookCount = 0,
      bookFileCount = 0,
      totalBookCount = 0,
      sizeOnDisk = 0
    } = statistics;

    const {
      hasBannerError,
      isEditAuthorModalOpen,
      isDeleteAuthorModalOpen
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
                <AuthorStatusCell
                  key={name}
                  className={styles[name]}
                  monitored={monitored}
                  status={status}
                  component={VirtualTableRowCell}
                />
              );
            }

            if (name === 'sortName') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={classNames(
                    styles[name],
                    showBanners && styles.banner,
                    showBanners && !hasGrowableColumns(columns) && styles.bannerGrow
                  )}
                >
                  {
                    showBanners ?
                      <Link
                        className={styles.link}
                        to={`/author/${titleSlug}`}
                      >
                        <AuthorBanner
                          className={styles.bannerImage}
                          images={images}
                          lazy={false}
                          overflow={true}
                          onError={this.onBannerLoadError}
                          onLoad={this.onBannerLoad}
                        />

                        {
                          hasBannerError &&
                            <div className={styles.overlayTitle}>
                              {showTitle === 'firstLast' ? authorName : authorNameLastFirst}
                            </div>
                        }
                      </Link> :

                      <AuthorNameLink
                        titleSlug={titleSlug}
                        authorName={showTitle === 'firstLast' ? authorName : authorNameLastFirst}
                      />
                  }
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

            if (name === 'metadataProfileId') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {metadataProfile?.name ?? ''}
                </VirtualTableRowCell>
              );
            }

            if (name === 'nextBook') {
              if (nextBook) {
                return (
                  <VirtualTableRowCell
                    key={name}
                    className={styles[name]}
                  >
                    <BookTitleLink
                      title={nextBook.title}
                      disambiguation={nextBook.disambiguation}
                      titleSlug={nextBook.titleSlug}
                    />
                  </VirtualTableRowCell>
                );
              }
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  None
                </VirtualTableRowCell>
              );
            }

            if (name === 'lastBook') {
              if (lastBook) {
                return (
                  <VirtualTableRowCell
                    key={name}
                    className={styles[name]}
                  >
                    <BookTitleLink
                      title={lastBook.title}
                      disambiguation={lastBook.disambiguation}
                      titleSlug={lastBook.titleSlug}
                    />
                  </VirtualTableRowCell>
                );
              }
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  None
                </VirtualTableRowCell>
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

            if (name === 'bookProgress') {
              const progress = bookCount ? (availableBookCount / bookCount) * 100 : 100;

              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <ProgressBar
                    progress={progress}
                    kind={getProgressBarKind(status, monitored, progress)}
                    showText={true}
                    text={`${availableBookCount} / ${bookCount}`}
                    title={translate('AuthorProgressBarText', { bookCount, availableBookCount, bookFileCount, totalBookCount })}
                    width={125}
                  />
                </VirtualTableRowCell>
              );
            }

            if (name === 'path') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {path}
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
                    title={translate('RefreshAuthor')}
                    isSpinning={isRefreshingAuthor}
                    onPress={onRefreshAuthorPress}
                  />

                  {
                    showSearchAction &&
                      <SpinnerIconButton
                        name={icons.SEARCH}
                        title={translate('SearchForMonitoredBooks')}
                        isSpinning={isSearchingAuthor}
                        onPress={onSearchPress}
                      />
                  }

                  <IconButton
                    name={icons.EDIT}
                    title={translate('EditAuthor')}
                    onPress={this.onEditAuthorPress}
                  />
                </VirtualTableRowCell>
              );
            }

            return null;
          })
        }

        <EditAuthorModalConnector
          isOpen={isEditAuthorModalOpen}
          authorId={id}
          onModalClose={this.onEditAuthorModalClose}
          onDeleteAuthorPress={this.onDeleteAuthorPress}
        />

        <DeleteAuthorModal
          isOpen={isDeleteAuthorModalOpen}
          authorId={id}
          onModalClose={this.onDeleteAuthorModalClose}
        />
      </>
    );
  }
}

export default AuthorIndexRow;
