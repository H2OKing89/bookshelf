import { Component } from 'react';
import type { BookId } from 'Book/BookIdentity';
import type Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import type { TableSelectionChange } from 'Components/Table/tableSelection';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import type { StateRecord } from 'Store/StateTypes';
import hasDifferentItemsOrOrder from 'Utilities/Object/hasDifferentItemsOrOrder';
import type { SelectedState } from 'Utilities/Table/selectionTypes';
import BookRowConnector from './BookRowConnector';
import { type DetailBookRow,monitorRange } from './detailRowsState';
import styles from './AuthorDetailsSeason.css';

interface CommonProps {
  items: readonly DetailBookRow[];
  columns: readonly Column[];
  sortKey?: string;
  sortDirection?: SortDirection;
  onSortPress: (sortKey: string) => void;
  onTableOptionChange: (payload: StateRecord) => void;
  onMonitorBookPress: (ids: BookId[], monitored: boolean) => void;
}
export interface AuthorDetailsSeasonProps extends CommonProps {
  isEditorActive: boolean;
  selectedState: SelectedState;
  setSelectedState: (items: readonly DetailBookRow[]) => void;
  onSelectedChange: (items: readonly DetailBookRow[], id: BookId, value: boolean | null, shiftKey: boolean) => void;
}
interface State { lastToggledBook: BookId | null; }

class AuthorDetailsSeason extends Component<AuthorDetailsSeasonProps, State> {

  //
  // Lifecycle

  constructor(props: AuthorDetailsSeasonProps) {
    super(props);

    this.state = {
      lastToggledBook: null
    };
  }

  componentDidMount() {
    this.props.setSelectedState(this.props.items);
  }

  componentDidUpdate(prevProps: AuthorDetailsSeasonProps) {
    const {
      items,
      sortKey,
      sortDirection,
      setSelectedState
    } = this.props;

    if (sortKey !== prevProps.sortKey ||
        sortDirection !== prevProps.sortDirection ||
        hasDifferentItemsOrOrder(prevProps.items, items)
    ) {
      setSelectedState(items);
    }
  }

  //
  // Listeners

  onMonitorBookPress = (bookId: BookId, monitored: boolean, { shiftKey }: { shiftKey?: boolean }) => {
    const bookIds = monitorRange(this.props.items, bookId, this.state.lastToggledBook, shiftKey);
    this.setState({ lastToggledBook: bookId });
    this.props.onMonitorBookPress(bookIds, monitored);
  };

  onSelectedChange = ({ id, value, shiftKey = false }: TableSelectionChange<BookId>) => {
    const {
      onSelectedChange,
      items
    } = this.props;

    return onSelectedChange(items, id, value, shiftKey);
  };

  //
  // Render

  render() {
    const {
      items,
      isEditorActive,
      columns,
      sortKey,
      sortDirection,
      onSortPress,
      onTableOptionChange,
      selectedState
    } = this.props;

    let titleColumns = columns;
    if (!isEditorActive) {
      titleColumns = columns.filter((x) => x.name !== 'select');
    }

    return (
      <div
        className={styles.bookType}
      >
        <div className={styles.books}>
          <Table
            columns={titleColumns}
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
                      onMonitorBookPress={this.onMonitorBookPress}
                      isEditorActive={isEditorActive}
                      isSelected={selectedState[item.id] === true}
                      onSelectedChange={this.onSelectedChange}
                    />
                  );
                })
              }
            </TableBody>
          </Table>
        </div>
      </div>
    );
  }
}

export default AuthorDetailsSeason;
