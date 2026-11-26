import moment from 'moment';
import { Component } from 'react';
import AuthorNameLink from 'Author/AuthorNameLink';
import BookCover from 'Book/BookCover';
import type { LibraryImage } from 'Book/LibraryPresentation';
import Alert from 'Components/Alert';
import HeartRating from 'Components/HeartRating';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Marquee from 'Components/Marquee';
import Measure from 'Components/Measure';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import TextTruncate from 'Components/TextTruncate';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import fonts from 'Styles/Variables/fonts';
import formatBytes from 'Utilities/Number/formatBytes';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import BookDetailsLinks from './BookDetailsLinks';
import type { authorReference, bookHeaderData } from './detailPresentation';
import styles from './BookDetailsHeader.css';

const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

function getFanartUrl(images: readonly LibraryImage[]) {
  return images.find((x) => x.coverType === 'fanart')?.url;
}

export type BookDetailsHeaderProps = ReturnType<typeof bookHeaderData> & {
  width: number;
  shortDateFormat: string;
  author: ReturnType<typeof authorReference>;
  isSmallScreen: boolean;
  onMonitorTogglePress: (monitored: boolean) => void;
};
interface State { overviewHeight: number; }

class BookDetailsHeader extends Component<BookDetailsHeaderProps, State> {

  //
  // Lifecycle

  constructor(props: BookDetailsHeaderProps) {
    super(props);

    this.state = {
      overviewHeight: 0
    };
  }

  //
  // Listeners

  onOverviewMeasure = ({ height }: { height: number }) => {
    this.setState({ overviewHeight: height });
  };

  //
  // Render

  render() {
    const {
      width,
      title,
      displayTitle,
      subtitle,
      displaySubtitle,
      seriesTitle,
      displaySeriesTitle,
      metadataLabels = [],
      metadataWarnings = [],
      pageCount,
      narrators = [],
      runtimeLengthMinutes,
      overview,
      statistics = {},
      monitored,
      releaseDate,
      ratings,
      images,
      links,
      isSaving = false,
      shortDateFormat,
      author,
      isSmallScreen,
      onMonitorTogglePress
    } = this.props;

    const {
      overviewHeight
    } = this.state;

    const fanartUrl = getFanartUrl(author?.images ?? []);
    const visibleSubtitle = displaySubtitle === undefined ? subtitle : displaySubtitle;
    const visibleSeries = displaySeriesTitle === undefined ? seriesTitle : displaySeriesTitle;

    return (
      <div className={styles.headerContainer} style={{ width }}>
        <div className={styles.header}>
          <div
            className={styles.backdrop}
            style={
              fanartUrl ?
                { backgroundImage: `url(${fanartUrl})` } :
                undefined
            }
          >
            <div className={styles.backdropOverlay} />
          </div>

          <div className={styles.headerContent}>
            <BookCover
              className={styles.cover}
              images={images}
              size={250}
              lazy={false}
            />

            <div className={styles.info}>
              <div className={styles.titleRow}>
                <div className={styles.titleContainer}>
                  <div className={styles.toggleMonitoredContainer}>
                    <MonitorToggleButton
                      aria-label={translate('MonitorBook')}
                      className={styles.monitorToggleButton}
                      monitored={monitored}
                      isSaving={isSaving}
                      size={isSmallScreen ? 30 : 40}
                      onPress={onMonitorTogglePress}
                    />
                  </div>

                  <div className={styles.title}>
                    {isSmallScreen ? displayTitle || title : <Marquee text={displayTitle || title} />}
                  </div>

                </div>
              </div>

              <div className={styles.details}>
                {
                  displayTitle && displayTitle !== title &&
                    <div>{translate('PublishedTitle', { 0: title })}</div>
                }

                {
                  visibleSubtitle &&
                    <div>{visibleSubtitle}</div>
                }

                <div>
                  {visibleSeries === (displayTitle || title) ? null : visibleSeries}
                </div>

                <div>
                  <AuthorNameLink
                    className={styles.authorLink}
                    titleSlug={author.titleSlug}
                    authorName={author.authorName}
                  />

                  {
                    !!runtimeLengthMinutes &&
                      <span className={styles.duration}>
                        {translate('RuntimeMinutes', { 0: runtimeLengthMinutes })}
                      </span>
                  }

                  {
                    !runtimeLengthMinutes && !!pageCount &&
                      <span className={styles.duration}>
                        {`${pageCount} pages`}
                      </span>
                  }

                  <HeartRating
                    rating={ratings.value}
                    iconSize={20}
                  />
                </div>

                {
                  narrators.length > 0 &&
                    <div>
                      {translate('NarratedBy', { 0: narrators.join(', ') })}
                    </div>
                }
              </div>

              <div>
                {
                  metadataLabels.map((label) => (
                    <Label key={label} className={styles.detailsLabel} size={sizes.LARGE}>
                      {label}
                    </Label>
                  ))
                }
                {
                  releaseDate &&
                    <Label
                      className={styles.detailsLabel}
                      size={sizes.LARGE}
                    >
                      <Icon
                        name={icons.CALENDAR}
                        size={17}
                      />

                      <span className={styles.sizeOnDisk}>
                        {
                          moment(releaseDate).format(shortDateFormat)
                        }
                      </span>
                    </Label>
                }

                <Label
                  className={styles.detailsLabel}
                  size={sizes.LARGE}
                >
                  <Icon
                    name={icons.DRIVE}
                    size={17}
                  />

                  <span className={styles.sizeOnDisk}>
                    {
                      formatBytes(statistics.sizeOnDisk)
                    }
                  </span>
                </Label>

                <Label
                  className={styles.detailsLabel}
                  size={sizes.LARGE}
                >
                  <Icon
                    name={monitored ? icons.MONITORED : icons.UNMONITORED}
                    size={17}
                  />

                  <span className={styles.qualityProfileName}>
                    {monitored ? 'Monitored' : 'Unmonitored'}
                  </span>
                </Label>

                <Tooltip
                  anchor={
                    <Label
                      className={styles.detailsLabel}
                      size={sizes.LARGE}
                    >
                      <Icon
                        name={icons.EXTERNAL_LINK}
                        size={17}
                      />

                      <span className={styles.links}>
                        Links
                      </span>
                    </Label>
                  }
                  tooltip={
                    <BookDetailsLinks
                      links={links}
                    />
                  }
                  kind={kinds.INVERSE}
                  position={tooltipPositions.BOTTOM}
                />

              </div>
              <Measure
                onMeasure={this.onOverviewMeasure}
                className={styles.overview}
              >
                <TextTruncate
                  line={Math.floor(overviewHeight / (defaultFontSize * lineHeight))}
                  text={stripHtml(overview)}
                />
              </Measure>
            </div>
          </div>
        </div>
        {
          metadataWarnings.map((warning) => (
            <Alert key={warning} kind={kinds.WARNING} role="status">
              {warning}
            </Alert>
          ))
        }
      </div>
    );
  }
}

export default BookDetailsHeader;
