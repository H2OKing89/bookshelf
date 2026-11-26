import { Component } from 'react';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import type Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { icons } from 'Helpers/Props';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import type { StateRecord } from 'Store/StateTypes';
import { summarizeSeriesRecordings } from 'Utilities/Book/recordingIdentity';
import translate from 'Utilities/String/translate';
import BookRowConnector from './BookRowConnector';
import { type DetailBookRow, monitorRange, type PositionMap } from './detailRowsState';
import styles from './AuthorDetailsSeries.css';

interface CommonProps {
  items: readonly DetailBookRow[];
  columns: readonly Column[];
  sortKey?: string;
  sortDirection?: SortDirection;
  onSortPress: (sortKey: string) => void;
  onTableOptionChange: (payload: StateRecord) => void;
  onMonitorBookPress: (ids: BookId[], monitored: boolean) => void;
}
export interface AuthorDetailsSeriesProps extends CommonProps {
  id: number;
  authorId: AuthorId;
  label: string;
  positionMap: PositionMap;
  isExpanded?: boolean;
  isSmallScreen: boolean;
  authorMonitored: boolean;
  onExpandPress: (id: number, expanded: boolean) => void;
}
interface State { isOrganizeModalOpen: boolean; isManageBooksOpen: boolean; lastToggledBook: BookId | null; }

class AuthorDetailsSeries extends Component<AuthorDetailsSeriesProps, State> {

  //
  // Lifecycle

  constructor(props: AuthorDetailsSeriesProps) {
    super(props);

    this.state = {
      isOrganizeModalOpen: false,
      isManageBooksOpen: false,
      lastToggledBook: null
    };
  }

  componentDidMount() {
    this._expandByDefault();
  }

  componentDidUpdate(prevProps: AuthorDetailsSeriesProps) {
    const {
      authorId
    } = this.props;

    if (prevProps.authorId !== authorId) {
      this._expandByDefault();
      return;
    }
  }

  //
  // Control

  _expandByDefault() {
    const {
      id,
      onExpandPress
    } = this.props;

    onExpandPress(id, true);
  }

  isSeriesMonitored(series: AuthorDetailsSeriesProps) {
    return series.items.every((book) => book.monitored);
  }

  isSeriesSaving(series: AuthorDetailsSeriesProps) {
    return series.items.some((book) => book.isSaving);
  }

  //
  // Listeners

  onExpandPress = () => {
    const {
      id,
      isExpanded
    } = this.props;

    this.props.onExpandPress(id, !isExpanded);
  };

  onMonitorBookPress = (bookId: BookId, monitored: boolean, { shiftKey }: { shiftKey?: boolean }) => {
    const bookIds = monitorRange(this.props.items, bookId, this.state.lastToggledBook, shiftKey);
    this.setState({ lastToggledBook: bookId });
    this.props.onMonitorBookPress(bookIds, monitored);
  };

  onMonitorSeriesPress = (monitored: boolean) => {
    const bookIds = this.props.items.map((book) => book.id);

    this.props.onMonitorBookPress([...new Set(bookIds)], monitored);
  };

  //
  // Render

  render() {
    const {
      label,
      items,
      positionMap,
      columns,
      isExpanded,
      sortKey,
      sortDirection,
      onSortPress,
      isSmallScreen,
      onTableOptionChange,
      authorMonitored
    } = this.props;

    const recordings = summarizeSeriesRecordings(items, positionMap);
    const recordingCount = recordings && (recordings.volumeCount == null ?
      translate('SeriesRecordingCount', { recordingCount: recordings.recordingCount }) :
      translate('SeriesVolumesAndRecordings', { volumeCount: recordings.volumeCount, recordingCount: recordings.recordingCount }));

    return (
      <div
        className={styles.bookType}
      >
        <div className={styles.seriesTitle}>
          <MonitorToggleButton
            aria-label={translate('MonitorSeries')}
            size={24}
            monitored={this.isSeriesMonitored(this.props)}
            isDisabled={!authorMonitored}
            isSaving={this.isSeriesSaving(this.props)}
            onPress={this.onMonitorSeriesPress}
          />

          <Link
            className={styles.expandButton}
            onPress={this.onExpandPress}
          >
            <div className={styles.header}>
              <div className={styles.left}>
                {
                  <div>
                    <span className={styles.bookTypeLabel}>
                      {label}
                    </span>

                    <span className={styles.bookCount}>
                      ({recordingCount || `${items.length} Books`})
                    </span>
                  </div>
                }

              </div>

              <Icon
                className={styles.expandButtonIcon}
                name={isExpanded ? icons.COLLAPSE : icons.EXPAND}
                title={isExpanded ? translate('IsExpandedHideBooks') : translate('IsExpandedShowBooks')}
                size={24}
              />

              {
                !isSmallScreen &&
                  <span>&nbsp;</span>
              }

            </div>
          </Link>
        </div>

        {
          recordings &&
            <div className={styles.recordingSummary}>
              <span>
                {translate('RecordingCatalogLabel', { marketplace: recordings.marketplaces.join(', ') })}
              </span>
              {
                recordings.ownedVolumeCount != null &&
                  <span>{translate('SeriesVolumesWithFiles', { ownedVolumeCount: recordings.ownedVolumeCount, volumeCount: recordings.volumeCount ?? 0 })}</span>
              }
              {
                recordings.hasAlternates &&
                  <span>{translate('SeriesAlternateRecordingStatus')}</span>
              }
            </div>
        }

        <div>
          {
            isExpanded &&
              <div className={styles.books}>
                <Table
                  columns={columns}
                  sortKey={sortKey}
                  sortDirection={sortDirection}
                  onSortPress={onSortPress}
                  onTableOptionChange={onTableOptionChange}
                >
                  <TableBody>
                    {
                      items.map((item) => {
                        return (
                          <BookRowConnector
                            key={item.id}
                            columns={columns}
                            {...item}
                            position={positionMap[item.id]}
                            onMonitorBookPress={this.onMonitorBookPress}
                            onSelectedChange={() => undefined}
                            isEditorActive={false}
                          />
                        );
                      })
                    }
                  </TableBody>
                </Table>

                <div className={styles.collapseButtonContainer}>
                  <IconButton
                    iconClassName={styles.collapseButtonIcon}
                    name={icons.COLLAPSE}
                    size={20}
                    title={translate('HideBooks')}
                    onPress={this.onExpandPress}
                  />
                </div>
              </div>
          }
        </div>
      </div>
    );
  }
}

export default AuthorDetailsSeries;
