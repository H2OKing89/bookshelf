import { Component } from 'react';
import AuthorPoster from 'Author/AuthorPoster';
import { getAuthorStatusDetails } from 'Author/AuthorStatus';
import type { AuthorId } from 'Book/BookIdentity';
import type { authorHeaderData } from 'Book/Details/detailPresentation';
import type { LibraryImage } from 'Book/LibraryPresentation';
import HeartRating from 'Components/HeartRating';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Marquee from 'Components/Marquee';
import Measure from 'Components/Measure';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import TextTruncate from 'Components/TextTruncate';
import Popover from 'Components/Tooltip/Popover';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import QualityProfileName from 'Settings/Profiles/Quality/QualityProfileName';
import fonts from 'Styles/Variables/fonts';
import formatBytes from 'Utilities/Number/formatBytes';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import AuthorAlternateTitles from './AuthorAlternateTitles';
import AuthorDetailsLinks from './AuthorDetailsLinks';
import AuthorTagsConnector from './AuthorTagsConnector';
import styles from './AuthorDetailsHeader.css';

const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

function getFanartUrl(images: readonly LibraryImage[]) {
  return images.find((x) => x.coverType === 'fanart')?.url;
}

export type AuthorDetailsHeaderProps = ReturnType<typeof authorHeaderData> & {
  id: AuthorId;
  width: number;
  isSmallScreen: boolean;
  onMonitorTogglePress: (monitored: boolean) => void;
};
interface State { overviewHeight: number; titleWidth: number; }

class AuthorDetailsHeader extends Component<AuthorDetailsHeaderProps, State> {

  //
  // Lifecyle

  constructor(props: AuthorDetailsHeaderProps) {
    super(props);

    this.state = {
      overviewHeight: 0,
      titleWidth: 0
    };
  }

  //
  // Listeners

  onOverviewMeasure = ({ height }: { height: number }) => {
    this.setState({ overviewHeight: height });
  };

  onTitleMeasure = ({ width }: { width: number }) => {
    this.setState({ titleWidth: width });
  };

  //
  // Render

  render() {
    const {
      id,
      width,
      authorName,
      ratings,
      path,
      statistics,
      qualityProfileId,
      monitored,
      status,
      overview,
      links,
      images,
      alternateTitles,
      tags,
      isSaving,
      isSmallScreen,
      onMonitorTogglePress
    } = this.props;

    const {
      bookFileCount,
      sizeOnDisk
    } = statistics;

    const {
      overviewHeight,
      titleWidth
    } = this.state;

    const statusDetails = getAuthorStatusDetails(status);

    const fanartUrl = getFanartUrl(images);
    const marqueeWidth = titleWidth - (isSmallScreen ? 85 : 160);

    let bookFilesCountMessage = translate('BookFilesCountMessage');

    if (bookFileCount === 1) {
      bookFilesCountMessage = '1 book file';
    } else if ((bookFileCount ?? 0) > 1) {
      bookFilesCountMessage = `${bookFileCount} book files`;
    }

    return (
      <div className={styles.header} style={{ width }} >
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
          <AuthorPoster
            className={styles.poster}
            images={images}
            size={250}
            lazy={false}
          />

          <div className={styles.info}>
            <Measure
              className={styles.titleRow}
              onMeasure={this.onTitleMeasure}
            >
              <div className={styles.titleContainer}>
                <div className={styles.toggleMonitoredContainer}>
                  <MonitorToggleButton
                    aria-label={translate('MonitorAuthor')}
                    className={styles.monitorToggleButton}
                    monitored={monitored}
                    isSaving={isSaving}
                    size={isSmallScreen ? 30: 40}
                    onPress={onMonitorTogglePress}
                  />
                </div>

                <div className={styles.title} style={{ width: marqueeWidth }}>
                  <Marquee text={authorName} />
                </div>

                {
                  !!alternateTitles.length &&
                    <div className={styles.alternateTitlesIconContainer}>
                      <Popover
                        anchor={
                          <Icon
                            name={icons.ALTERNATE_TITLES}
                            size={20}
                          />
                        }
                        title={translate('AlternateTitles')}
                        body={<AuthorAlternateTitles alternateTitles={alternateTitles} />}
                        position={tooltipPositions.BOTTOM}
                      />
                    </div>
                }
              </div>
            </Measure>

            <div className={styles.details}>
              <div>
                <HeartRating
                  rating={ratings.value}
                  iconSize={20}
                />
              </div>
            </div>

            <div>
              <Label
                className={styles.detailsLabel}
                size={sizes.LARGE}
              >
                <Icon
                  name={icons.FOLDER}
                  size={17}
                />

                <span className={styles.path}>
                  {path}
                </span>
              </Label>

              <Label
                className={styles.detailsLabel}
                title={bookFilesCountMessage}
                size={sizes.LARGE}
              >
                <Icon
                  name={icons.DRIVE}
                  size={17}
                />

                <span className={styles.sizeOnDisk}>
                  {
                    formatBytes(sizeOnDisk || 0)
                  }
                </span>
              </Label>

              <Label
                className={styles.detailsLabel}
                title={translate('QualityProfile')}
                size={sizes.LARGE}
              >
                <Icon
                  name={icons.PROFILE}
                  size={17}
                />

                <span className={styles.qualityProfileName}>
                  {
                    <QualityProfileName
                      qualityProfileId={qualityProfileId}
                    />
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

              <Label
                className={styles.detailsLabel}
                title={statusDetails.message}
                size={sizes.LARGE}
              >
                <Icon
                  name={statusDetails.icon}
                  size={17}
                />

                <span className={styles.qualityProfileName}>
                  {statusDetails.title}
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
                  <AuthorDetailsLinks
                    links={links}
                  />
                }
                kind={kinds.INVERSE}
                position={tooltipPositions.BOTTOM}
              />

              {
                !!tags.length &&
                  <Tooltip
                    anchor={
                      <Label
                        className={styles.detailsLabel}
                        size={sizes.LARGE}
                      >
                        <Icon
                          name={icons.TAGS}
                          size={17}
                        />

                        <span className={styles.tags}>
                          Tags
                        </span>
                      </Label>
                    }
                    tooltip={<AuthorTagsConnector authorId={id} />}
                    kind={kinds.INVERSE}
                    position={tooltipPositions.BOTTOM}
                  />

              }
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
    );
  }
}

export default AuthorDetailsHeader;
