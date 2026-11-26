import React, { Component } from 'react';
import AuthorPoster from 'Author/AuthorPoster';
import DeleteAuthorModal from 'Author/Delete/DeleteAuthorModal';
import EditAuthorModalConnector from 'Author/Edit/EditAuthorModalConnector';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import BookIndexProgressBar from 'Book/Index/ProgressBar/BookIndexProgressBar';
import type { AuthorReference, CheckboxChange, LibraryImage, LibraryStatistics, NamedProfile, SelectionChange } from 'Book/LibraryPresentation';
import CheckInput from 'Components/Form/CheckInput';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import TextTruncate from 'Components/TextTruncate';
import { icons } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import fonts from 'Styles/Variables/fonts';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import BookIndexOverviewInfo from './BookIndexOverviewInfo';
import styles from './BookIndexOverview.css';

interface OverviewOptions {
  detailedProgressBar: boolean;
  showMonitored: boolean;
  showQualityProfile: boolean;
  showAdded: boolean;
  showPath: boolean;
  showSizeOnDisk: boolean;
  showReleaseDate: boolean;
  showFileQualityRank?: boolean;
}

interface BookIndexOverviewProps {
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
  posterWidth: number;
  posterHeight: number;
  showRelativeDates: boolean;
  shortDateFormat: string;
  timeFormat: string;
  rowHeight: number;
  isSmallScreen: boolean;
  longDateFormat: string;
  overviewOptions: OverviewOptions;
}

interface BookIndexOverviewState {
  isEditAuthorModalOpen: boolean;
  isDeleteAuthorModalOpen: boolean;
  overview: string;
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

class BookIndexOverview extends Component<BookIndexOverviewProps, BookIndexOverviewState> {
  static defaultProps = {
    statistics: {
      bookCount: 0,
      bookFileCount: 0,
      totalBookCount: 0
    }
  };

  //
  // Lifecycle

  constructor(props: BookIndexOverviewProps) {
    super(props);

    this.state = {
      isEditAuthorModalOpen: false,
      isDeleteAuthorModalOpen: false,
      overview: ''
    };
  }

  componentDidMount() {
    const { id } = this.props;

    // Note that this component is lazy loaded by the virtualised view.
    // We want to avoid storing overviews for *all* books which is
    // why it's not put into the redux store
    const promise = createAjaxRequest({
      url: `/book/${id}/overview`
    }).request;

    promise.done((data: unknown) => {
      if (data && typeof data === 'object' && 'overview' in data) {
        const overview = data.overview;
        if (typeof overview === 'string' || overview == null) {
          this.setState({ overview: typeof overview === 'string' ? overview : '' });
        }
      }
    });
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
      authorId,
      title,
      displayTitle,
      monitored,
      titleSlug,
      statistics,
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
      isRefreshingBook,
      isSearchingBook,
      onRefreshBookPress,
      onSearchPress,
      isEditorActive,
      isSelected,
      ...otherProps
    } = this.props;

    const {
      bookCount = 0,
      sizeOnDisk = 0,
      bookFileCount = 0,
      totalBookCount = 0
    } = statistics;

    const {
      overview,
      isEditAuthorModalOpen,
      isDeleteAuthorModalOpen
    } = this.state;

    const link = `/book/${titleSlug}`;

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

            <Link
              className={styles.link}
              style={elementStyle}
              to={link}
            >
              <AuthorPoster
                className={styles.poster}
                style={elementStyle}
                images={images}
                coverType={'cover'}
                size={250}
                lazy={false}
                overflow={true}
                blurBackground={true}
              />
            </Link>

            <BookIndexProgressBar
              monitored={monitored}
              bookCount={bookCount}
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
                {displayTitle || title}
              </Link>

              <div className={styles.actions}>
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

              <BookIndexOverviewInfo
                height={overviewHeight}
                monitored={monitored}
                sizeOnDisk={sizeOnDisk}
                fileQualityRank={statistics.fileQualityRank}
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
          authorId={authorId}
          onModalClose={this.onEditAuthorModalClose}
          onDeleteAuthorPress={this.onDeleteAuthorPress}
        />

        <DeleteAuthorModal
          isOpen={isDeleteAuthorModalOpen}
          authorId={authorId}
          onModalClose={this.onDeleteAuthorModalClose}
        />
      </div>
    );
  }
}

export default BookIndexOverview;
