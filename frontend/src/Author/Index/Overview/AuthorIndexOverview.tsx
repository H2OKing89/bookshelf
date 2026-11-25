import React, { Component } from 'react';
import TextTruncate from 'react-text-truncate';
import type { AuthorStatus } from 'Author/Author';
import AuthorPoster from 'Author/AuthorPoster';
import DeleteAuthorModal from 'Author/Delete/DeleteAuthorModal';
import EditAuthorModalConnector from 'Author/Edit/EditAuthorModalConnector';
import AuthorIndexProgressBar from 'Author/Index/ProgressBar/AuthorIndexProgressBar';
import type { AuthorId } from 'Book/BookIdentity';
import type { BookReference, CheckboxChange, LibraryImage, LibraryStatistics, NamedProfile, SelectionChange } from 'Book/LibraryPresentation';
import CheckInput from 'Components/Form/CheckInput';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import { icons } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import fonts from 'Styles/Variables/fonts';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import AuthorIndexOverviewInfo from './AuthorIndexOverviewInfo';
import styles from './AuthorIndexOverview.css';

interface OverviewOptions {
  detailedProgressBar: boolean;
  showMonitored: boolean;
  showQualityProfile: boolean;
  showAdded: boolean;
  showPath: boolean;
  showSizeOnDisk: boolean;
  showBookCount: boolean;
  showLastBook?: boolean;
  showTitle: string;
}

interface AuthorIndexOverviewProps {
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
  rowHeight: number;
  isSmallScreen: boolean;
  longDateFormat: string;
  overviewOptions: OverviewOptions;
  overview?: string | null;
}

interface AuthorIndexOverviewState {
  isEditAuthorModalOpen: boolean;
  isDeleteAuthorModalOpen: boolean;
}

const columnPadding = parseInt(dimensions.authorIndexColumnPadding);
const columnPaddingSmallScreen = parseInt(dimensions.authorIndexColumnPaddingSmallScreen);
const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

// Hardcoded height beased on line-height of 32 + bottom margin of 10.
// Less side-effecty than using react-measure.
const titleRowHeight = 42;

function getContentHeight(rowHeight: number, isSmallScreen: boolean) {
  const padding = isSmallScreen ? columnPaddingSmallScreen : columnPadding;

  return rowHeight - padding;
}

class AuthorIndexOverview extends Component<AuthorIndexOverviewProps, AuthorIndexOverviewState> {
  static defaultProps = {
    statistics: {
      bookCount: 0,
      bookFileCount: 0,
      totalBookCount: 0
    }
  };

  //
  // Lifecycle

  constructor(props: AuthorIndexOverviewProps) {
    super(props);

    this.state = {
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
      overview,
      monitored,
      status,
      titleSlug,
      nextAiring,
      statistics = {},
      images,
      posterWidth,
      posterHeight,
      qualityProfile,
      overviewOptions,
      showSearchAction,
      showRelativeDates,
      shortDateFormat,
      longDateFormat,
      timeFormat,
      rowHeight,
      isSmallScreen,
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
      isEditAuthorModalOpen,
      isDeleteAuthorModalOpen
    } = this.state;

    const link = `/author/${titleSlug}`;

    const elementStyle: React.CSSProperties = {
      width: `${posterWidth}px`,
      height: `${posterHeight}px`,
      objectFit: 'contain'
    };

    const contentHeight = getContentHeight(rowHeight, isSmallScreen);
    const overviewHeight = contentHeight - titleRowHeight;

    return (
      <div className={styles.container}>
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
              />
            </Link>

            <AuthorIndexProgressBar
              monitored={monitored}
              status={status}
              bookCount={bookCount}
              availableBookCount={availableBookCount}
              bookFileCount={bookFileCount}
              totalBookCount={totalBookCount}
              posterWidth={posterWidth}
              detailedProgressBar={overviewOptions.detailedProgressBar}
            />
          </div>

          <div className={styles.info} style={{ maxHeight: contentHeight }}>
            <div className={styles.titleRow}>
              <Link
                className={styles.title}
                to={link}
              >
                {overviewOptions.showTitle === 'firstLast' ? authorName : authorNameLastFirst}
              </Link>

              <div className={styles.actions}>
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
              </div>
            </div>

            <div className={styles.details}>

              <Link
                className={styles.overview}
                to={link}
              >
                <TextTruncate
                  line={Math.floor(overviewHeight / (defaultFontSize * lineHeight))}
                  text={stripHtml(overview)}
                />
              </Link>

              <AuthorIndexOverviewInfo
                height={overviewHeight}
                monitored={monitored}
                nextAiring={nextAiring}
                bookCount={bookCount}
                sizeOnDisk={sizeOnDisk}
                qualityProfile={qualityProfile}
                showRelativeDates={showRelativeDates}
                shortDateFormat={shortDateFormat}
                longDateFormat={longDateFormat}
                timeFormat={timeFormat}
                {...overviewOptions}
                {...otherProps}
              />
            </div>
          </div>
        </div>

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
    );
  }
}

export default AuthorIndexOverview;
