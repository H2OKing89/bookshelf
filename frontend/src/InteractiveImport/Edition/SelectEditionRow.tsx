import _ from 'lodash';
import React, { Component } from 'react';
import type { BookId } from 'Book/BookIdentity';
import type { LibraryEdition } from 'Book/LibraryModels';
import type { LibraryColumn } from 'Book/LibraryPresentation';
import FormInputGroup from 'Components/Form/FormInputGroup';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { inputTypes } from 'Helpers/Props';
import titleCase from 'Utilities/String/titleCase';

interface SelectEditionRowProps {
  id: BookId;
  matchedEditionId: string;
  title: string;
  disambiguation?: string | null;
  editions: readonly LibraryEdition[];
  onEditionSelect: (id: BookId, foreignEditionId: string) => void;
  columns: readonly LibraryColumn[];
}

class SelectEditionRow extends Component<SelectEditionRowProps> {

  //
  // Listeners

  onInputChange = ({ name, value }: { name: string; value: unknown }) => {
    const { id, editions, onEditionSelect } = this.props;
    if (name === String(id) && typeof value === 'string' && value &&
        editions.some((edition) => edition.foreignEditionId === value && (!edition.bookId || edition.bookId === id))) {
      onEditionSelect(id, value);
    }
  };

  //
  // Render

  render() {
    const {
      id,
      matchedEditionId,
      title,
      disambiguation,
      editions,
      columns
    } = this.props;

    const extendedTitle = disambiguation ? `${title} (${disambiguation})` : title;

    const values = editions.flatMap((bookEdition) => {
      const key = bookEdition.foreignEditionId;
      if (!key || (bookEdition.bookId && bookEdition.bookId !== id)) {
        return [];
      }


      let value = `${bookEdition.title}`;

      if (bookEdition.disambiguation) {
        value = `${value} (${titleCase(bookEdition.disambiguation)})`;
      }

      const extras = [];
      if (bookEdition.language) {
        extras.push(bookEdition.language);
      }
      if (bookEdition.publisher) {
        extras.push(bookEdition.publisher);
      }
      if (bookEdition.isbn13) {
        extras.push(bookEdition.isbn13);
      }
      if (bookEdition.asin) {
        extras.push(bookEdition.asin);
      }
      if (bookEdition.audibleRegion) {
        extras.push(bookEdition.audibleRegion.toUpperCase());
      }
      if (bookEdition.format) {
        extras.push(bookEdition.format);
      }
      if ((bookEdition.pageCount ?? 0) > 0) {
        extras.push(`${bookEdition.pageCount}p`);
      }

      if (extras.length > 0) {
        value = `${value} [${extras.join(', ')}]`;
      }

      return [{ key, value }];
    });

    const sortedValues = _.orderBy(values, ['value']);

    return (
      <TableRow>
        {
          columns.map((column) => {
            const {
              name,
              isVisible
            } = column;

            if (!isVisible) {
              return null;
            }

            if (name === 'book') {
              return (
                <TableRowCell key={name}>
                  {extendedTitle}
                </TableRowCell>
              );
            }

            if (name === 'edition') {
              return (
                <TableRowCell key={name}>
                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name={id.toString()}
                    values={sortedValues}
                    value={matchedEditionId}
                    onChange={this.onInputChange}
                  />
                </TableRowCell>
              );
            }

            return null;
          })
        }
      </TableRow>

    );
  }
}


export default SelectEditionRow;
