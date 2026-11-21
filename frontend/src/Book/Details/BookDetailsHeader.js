import moment from 'moment';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextTruncate from 'react-text-truncate';
import AuthorNameLink from 'Author/AuthorNameLink';
import BookCover from 'Book/BookCover';
import Alert from 'Components/Alert';
import HeartRating from 'Components/HeartRating';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Marquee from 'Components/Marquee';
import Measure from 'Components/Measure';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import fonts from 'Styles/Variables/fonts';
import formatBytes from 'Utilities/Number/formatBytes';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import BookDetailsLinks from './BookDetailsLinks';
import styles from './BookDetailsHeader.css';

const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

function getFanartUrl(images) {
  return images.find((x) => x.coverType === 'fanart')?.url;
}

class BookDetailsHeader extends Component {

  //
  // Lifecycle

  constructor(props) {
    super(props);

    this.state = {
      overviewHeight: 0
    };
  }

  //
  // Listeners

  onOverviewMeasure = ({ height }) => {
    this.setState({ overviewHeight: height });
  };

  //
  // Render

  render() {
    const {
      width,
      titleSlug,
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
      isSaving,
      shortDateFormat,
      author,
      isSmallScreen,
      onMonitorTogglePress
    } = this.props;

    const {
      overviewHeight
    } = this.state;

    const fanartUrl = getFanartUrl(author.images);
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
                null
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
                    <div>{translate('PublishedTitle', [title])}</div>
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
                        {translate('RuntimeMinutes', [runtimeLengthMinutes])}
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
                      {translate('NarratedBy', [narrators.join(', ')])}
                    </div>
                }
              </div>

              <div className={styles.detailsLabels}>
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
                      titleSlug={titleSlug}
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

BookDetailsHeader.propTypes = {
  id: PropTypes.number.isRequired,
  width: PropTypes.number.isRequired,
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  displayTitle: PropTypes.string,
  subtitle: PropTypes.string,
  displaySubtitle: PropTypes.string,
  seriesTitle: PropTypes.string.isRequired,
  displaySeriesTitle: PropTypes.string,
  metadataLabels: PropTypes.arrayOf(PropTypes.string),
  metadataWarnings: PropTypes.arrayOf(PropTypes.string),
  pageCount: PropTypes.number,
  narrators: PropTypes.arrayOf(PropTypes.string),
  runtimeLengthMinutes: PropTypes.number,
  overview: PropTypes.string,
  statistics: PropTypes.object.isRequired,
  releaseDate: PropTypes.string,
  ratings: PropTypes.object.isRequired,
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  links: PropTypes.arrayOf(PropTypes.object).isRequired,
  monitored: PropTypes.bool.isRequired,
  shortDateFormat: PropTypes.string.isRequired,
  isSaving: PropTypes.bool.isRequired,
  author: PropTypes.object,
  isSmallScreen: PropTypes.bool.isRequired,
  onMonitorTogglePress: PropTypes.func.isRequired
};

BookDetailsHeader.defaultProps = {
  isSaving: false
};

export default BookDetailsHeader;
