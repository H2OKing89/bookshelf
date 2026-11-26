import type { AuthorReference, NamedProfile } from 'Book/LibraryPresentation';
import { icons } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import formatDateTime from 'Utilities/Date/formatDateTime';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import formatBytes from 'Utilities/Number/formatBytes';
import { formatRecordingQualityRank } from 'Utilities/Quality/qualityRank';
import translate from 'Utilities/String/translate';
import BookIndexOverviewInfoRow from './BookIndexOverviewInfoRow';
import styles from './BookIndexOverviewInfo.css';

interface BookIndexOverviewInfoProps {
  height: number;
  showMonitored: boolean;
  showQualityProfile: boolean;
  showAdded: boolean;
  showPath: boolean;
  showSizeOnDisk: boolean;
  qualityProfileId?: number | null;
  monitored: boolean;
  qualityProfile?: NamedProfile | null;
  added?: string | null;
  sizeOnDisk?: number | null;
  sortKey: string;
  showRelativeDates: boolean;
  shortDateFormat: string;
  longDateFormat: string;
  timeFormat: string;
  showReleaseDate: boolean;
  showFileQualityRank?: boolean;
  author: AuthorReference;
  releaseDate?: string | null;
  fileQualityRank?: number | null;
}

const infoRowHeight = parseInt(dimensions.authorIndexOverviewInfoRowHeight);

const rows = [
  {
    name: 'monitored',
    showProp: 'showMonitored',
    valueProp: 'monitored'

  },
  {
    name: 'qualityProfileId',
    showProp: 'showQualityProfile',
    valueProp: 'qualityProfile'
  },
  {
    name: 'releaseDate',
    showProp: 'showReleaseDate',
    valueProp: 'releaseDate'
  },
  {
    name: 'added',
    showProp: 'showAdded',
    valueProp: 'added'
  },
  {
    name: 'path',
    showProp: 'showPath',
    valueProp: 'author'
  },
  {
    name: 'fileQualityRank',
    showProp: 'showFileQualityRank',
    valueProp: 'fileQualityRank'
  },
  {
    name: 'sizeOnDisk',
    showProp: 'showSizeOnDisk',
    valueProp: 'sizeOnDisk'
  }
] as const;

type InfoRow = typeof rows[number];

function isVisible(row: InfoRow, props: BookIndexOverviewInfoProps): boolean {
  const {
    name,
    showProp,
    valueProp
  } = row;

  if (name === 'fileQualityRank') {
    return props.sortKey === name;
  }

  if (props[valueProp] == null) {
    return false;
  }

  return !!props[showProp] || props.sortKey === name;
}

function getInfoRowProps(row: InfoRow, props: BookIndexOverviewInfoProps) {
  const { name } = row;

  if (name === 'monitored') {
    const monitoredText = props.monitored ? 'Monitored' : 'Unmonitored';

    return {
      title: monitoredText,
      iconName: props.monitored ? icons.MONITORED : icons.UNMONITORED,
      label: monitoredText
    };
  }

  if (name === 'fileQualityRank') {
    return { title: 'Lowest file quality rank', iconName: icons.PROFILE, label: formatRecordingQualityRank(props.fileQualityRank) };
  }

  if (name === 'qualityProfileId' && !!props.qualityProfile?.name) {
    return {
      title: translate('QualityProfile'),
      iconName: icons.PROFILE,
      label: props.qualityProfile.name
    };
  }

  if (name === 'releaseDate') {
    const {
      releaseDate,
      showRelativeDates,
      shortDateFormat,
      longDateFormat,
      timeFormat
    } = props;

    return {
      title: `ReleaseDate: ${formatDateTime(releaseDate, longDateFormat, timeFormat)}`,
      iconName: icons.CALENDAR,
      label: getRelativeDate(
        releaseDate,
        shortDateFormat,
        showRelativeDates,
        {
          timeFormat,
          timeForToday: true
        }
      )
    };
  }

  if (name === 'added') {
    const {
      added,
      showRelativeDates,
      shortDateFormat,
      longDateFormat,
      timeFormat
    } = props;

    return {
      title: `Added: ${formatDateTime(added, longDateFormat, timeFormat)}`,
      iconName: icons.ADD,
      label: getRelativeDate(
        added,
        shortDateFormat,
        showRelativeDates,
        {
          timeFormat,
          timeForToday: true
        }
      )
    };
  }

  if (name === 'path') {
    return {
      title: 'Path',
      iconName: icons.FOLDER,
      label: props.author.path
    };
  }

  if (name === 'sizeOnDisk') {
    return {
      title: 'Size on Disk',
      iconName: icons.DRIVE,
      label: formatBytes(props.sizeOnDisk)
    };
  }
  return undefined;
}

function BookIndexOverviewInfo(props: BookIndexOverviewInfoProps) {
  const {
    height
  } = props;

  let shownRows = 1;

  const maxRows = Math.floor(height / (infoRowHeight + 4));

  return (
    <div className={styles.infos}>
      {
        rows.map((row) => {
          if (!isVisible(row, props)) {
            return null;
          }

          if (shownRows >= maxRows) {
            return null;
          }

          shownRows++;

          const infoRowProps = getInfoRowProps(row, props);

          if (!infoRowProps) {
            return null;
          }

          return (
            <BookIndexOverviewInfoRow
              key={row.name}
              {...infoRowProps}
            />
          );
        })
      }
    </div>
  );
}

export default BookIndexOverviewInfo;
