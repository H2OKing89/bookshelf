import _ from 'lodash';
import React, { Component } from 'react';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import type { CheckboxSelectionChange,TableSelectionChange } from 'Components/Table/tableSelection';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import selectAll from 'Utilities/Table/selectAll';
import type { SelectionFlags, SelectionState } from 'Utilities/Table/selectionTypes';
import toggleSelected from 'Utilities/Table/toggleSelected';
import formControlProps, { type FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import type { ProviderItem, ProviderKey } from './providerControlTypes';
import styles from './BookshelfInput.css';

interface Props extends FormControlProps {
  className?: string;
  name: string;
  value: readonly ProviderKey[];
  items: readonly ProviderItem[];
  hasError?: boolean;
  hasWarning?: boolean;
  isFetching: boolean;
  isPopulated: boolean;
  onChange: (change: ValueChange<ProviderKey[]>) => void;
  helptext: string;
  user: string;
}

const columns = [
  {
    name: 'name',
    label: 'Bookshelf',
    isSortable: false,
    isVisible: true
  }
];

class BookshelfInput extends Component<Props, SelectionState<ProviderKey> & SelectionFlags> {
  static defaultProps = { className: styles.bookshelfInputWrapper, isPopulated: false };

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    const initialSelection = _.mapValues(_.keyBy(props.value), () => true);

    this.state = {
      allSelected: false,
      allUnselected: false,
      selectedState: initialSelection
    };
  }

  componentDidUpdate(_prevProps: Props, prevState: SelectionState<ProviderKey> & SelectionFlags) {
    const {
      name,
      onChange
    } = this.props;

    const oldSelected = getSelectedIds(prevState.selectedState, { parseIds: false }).sort();
    const newSelected = this.getSelectedIds().sort();

    if (!_.isEqual(oldSelected, newSelected)) {
      onChange({
        name,
        value: newSelected
      });
    }
  }

  //
  // Control

  getSelectedIds = () => {
    return getSelectedIds(this.state.selectedState, { parseIds: false });
  };

  //
  // Listeners

  onSelectAllChange = ({ value }: CheckboxSelectionChange) => {
    this.setState(selectAll(this.state.selectedState, value));
  };

  onSelectedChange = ({ id, value, shiftKey = false }: TableSelectionChange<ProviderKey>) => {
    this.setState((state, props) => {
      return toggleSelected(state, props.items, id, value, shiftKey);
    });
  };

  //
  // Render

  render() {
    const {
      className,
      helptext,
      items,
      user,
      isFetching,
      isPopulated
    } = this.props;

    const {
      allSelected,
      allUnselected,
      selectedState
    } = this.state;

    return (
      <div
        {...formControlProps(this.props)}
        className={className}
        role="group"
      >
        {
          isFetching &&
            <LoadingIndicator />
        }

        {
          !isPopulated && !isFetching &&
            <div>
              Authenticate with Goodreads to retrieve bookshelves.
            </div>
        }

        {
          isPopulated && !isFetching && !user &&
            <div>
              Could not retrieve data from Goodreads.  Try re-authenticating.
            </div>
        }

        {
          isPopulated && !isFetching && user && !items.length &&
            <div>
              No bookshelves found for Goodreads user {user}.
            </div>
        }

        {
          isPopulated && !isFetching && user && !!items.length &&
            <div className={className}>
              {helptext}
              <Table
                columns={columns}
                selectAll={true}
                allSelected={allSelected}
                allUnselected={allUnselected}
                onSelectAllChange={this.onSelectAllChange}
              >
                <TableBody>
                  {
                    items.map((item) => {
                      return (
                        <TableRow
                          key={item.id}
                        >
                          <TableSelectCell
                            id={item.id}
                            isSelected={selectedState[item.id] ?? false}
                            onSelectedChange={this.onSelectedChange}
                          />

                          <TableRowCell
                            title={item.name}
                          >
                            {item.name}
                          </TableRowCell>
                        </TableRow>
                      );
                    })
                  }
                </TableBody>
              </Table>
            </div>
        }
      </div>
    );
  }
}

export default BookshelfInput;
