import React, { Component } from 'react';
import type { AuthorStatus } from 'Author/Author';
import AuthorPoster from 'Author/AuthorPoster';
import DeleteAuthorModal from 'Author/Delete/DeleteAuthorModal';
import EditAuthorModalConnector from 'Author/Edit/EditAuthorModalConnector';
import AuthorIndexProgressBar from 'Author/Index/ProgressBar/AuthorIndexProgressBar';
import type { AuthorId } from 'Book/BookIdentity';
import type { BookReference, CheckboxChange, LibraryImage, LibraryStatistics, NamedProfile, SelectionChange } from 'Book/LibraryPresentation';
import CheckInput from 'Components/Form/CheckInput';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import { icons } from 'Helpers/Props';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import translate from 'Utilities/String/translate';
import AuthorIndexPosterInfo from './AuthorIndexPosterInfo';
import styles from './AuthorIndexPoster.css';

interface AuthorIndexPosterProps {
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
  posterWidth: number;
  posterHeight: number;
  showRelativeDates: boolean;
  shortDateFormat: string;
  timeFormat: string;
  detailedProgressBar: boolean;
  showTitle: string;
  showMonitored: boolean;
  showQualityProfile: boolean;
}

interface AuthorIndexPosterState {
  isEditAuthorModalOpen: boolean;
  isDeleteAuthorModalOpen: boolean;
  hasPosterError: boolean;
}

class AuthorIndexPoster extends Component<AuthorIndexPosterProps, AuthorIndexPosterState> {
  static defaultProps = {
    statistics: {
      bookCount: 0,
      bookFileCount: 0,
      totalBookCount: 0
    }
  };

  //
  // Lifecycle

  constructor(props: AuthorIndexPosterProps) {
    super(props);

    this.state = {
      hasPosterError: false,
      isEditAuthorModalOpen: false,
      isDeleteAuthorModalOpen: false
    };
  }

  //
  // Listeners

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

  onPosterLoad = () => {
    if (this.state.hasPosterError) {
      this.setState({ hasPosterError: false });
    }
  };

  onPosterLoadError = () => {
    if (!this.state.hasPosterError) {
      this.setState({ hasPosterError: true });
    }
  };

  onChange = ({ value, shiftKey }: CheckboxChange) => {
    const {
      id,
      onSelectedChange
    } = this.props;

    onSelectedChange({ id, value, shiftKey });
  };

  //
  // Render

  render() {
    const {
      id,
      authorName,
      authorNameLastFirst,
      monitored,
      titleSlug,
      status,
      nextAiring,
      statistics = {},
      images,
      posterWidth,
      posterHeight,
      detailedProgressBar,
      showTitle,
      showMonitored,
      showQualityProfile,
      qualityProfile,
      metadataProfile,
      showSearchAction,
      showRelativeDates,
      shortDateFormat,
      timeFormat,
      isRefreshingAuthor,
      isSearchingAuthor,
      onRefreshAuthorPress,
      onSearchPress,
      isEditorActive,
      isSelected,
      ...otherProps
    } = this.props;

    const {
      bookCount = 0,
      availableBookCount = 0,
      bookFileCount = 0,
      totalBookCount = 0,
      sizeOnDisk = 0
    } = statistics;

    const {
      hasPosterError,
      isEditAuthorModalOpen,
      isDeleteAuthorModalOpen
    } = this.state;

    const link = `/author/${titleSlug}`;

    const elementStyle: React.CSSProperties = {
      width: `${posterWidth}px`,
      height: `${posterHeight}px`,
      objectFit: 'contain'
    };

    return (
      <div>
        <div className={styles.content}>
          <div className={styles.posterContainer}>
            {
              isEditorActive &&
                <div className={styles.editorSelect}>
                  <CheckInput
                    name={id.toString()}
                    value={isSelected ?? false}
                    onChange={this.onChange}
                  />
                </div>
            }

            <Label className={styles.controls}>
              <SpinnerIconButton
                className={styles.action}
                name={icons.REFRESH}
                title={translate('RefreshAuthor')}
                isSpinning={isRefreshingAuthor}
                onPress={onRefreshAuthorPress}
              />

              {
                showSearchAction &&
                  <SpinnerIconButton
                    className={styles.action}
                    name={icons.SEARCH}
                    title={translate('SearchForMonitoredBooks')}
                    isSpinning={isSearchingAuthor}
                    onPress={onSearchPress}
                  />
              }

              <IconButton
                className={styles.action}
                name={icons.EDIT}
                title={translate('EditAuthor')}
                onPress={this.onEditAuthorPress}
              />
            </Label>

            {
              status === 'ended' &&
                <div
                  className={styles.ended}
                  title={translate('Ended')}
                />
            }

            <Link
              className={styles.link}
              style={elementStyle}
              to={link}
            >
              <AuthorPoster
                className={styles.poster}
                style={elementStyle}
                images={images}
                size={250}
                lazy={false}
                overflow={true}
                blurBackground={true}
                onError={this.onPosterLoadError}
                onLoad={this.onPosterLoad}
              />

              {
                hasPosterError &&
                  <div className={styles.overlayTitle}>
                    {authorName}
                  </div>
              }

            </Link>
          </div>

          <AuthorIndexProgressBar
            monitored={monitored}
            status={status}
            bookCount={bookCount}
            availableBookCount={availableBookCount}
            bookFileCount={bookFileCount}
            totalBookCount={totalBookCount}
            posterWidth={posterWidth}
            detailedProgressBar={detailedProgressBar}
          />

          {
            showTitle !== 'no' &&
              <div className={styles.title}>
                {showTitle === 'firstLast' ? authorName : authorNameLastFirst}
              </div>
          }

          {
            showMonitored &&
              <div className={styles.title}>
                {monitored ? 'Monitored' : 'Unmonitored'}
              </div>
          }

          {showQualityProfile && !!qualityProfile?.name ? (
            <div className={styles.title} title={translate('QualityProfile')}>
              {qualityProfile?.name}
            </div>
          ) : null}

          {
            nextAiring &&
              <div className={styles.nextAiring}>
                {
                  getRelativeDate(
                    nextAiring,
                    shortDateFormat,
                    showRelativeDates,
                    {
                      timeFormat,
                      timeForToday: true
                    }
                  )
                }
              </div>
          }
          <AuthorIndexPosterInfo
            bookCount={bookCount}
            sizeOnDisk={sizeOnDisk}
            qualityProfile={qualityProfile}
            showQualityProfile={showQualityProfile}
            metadataProfile={metadataProfile}
            showRelativeDates={showRelativeDates}
            shortDateFormat={shortDateFormat}
            timeFormat={timeFormat}
            {...otherProps}
          />

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
        </div>
      </div>
    );
  }
}

export default AuthorIndexPoster;
