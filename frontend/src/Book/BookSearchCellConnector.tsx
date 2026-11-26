import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import type { BookId } from 'Book/BookIdentity';
import * as commandNames from 'Commands/commandNames';
import { executeCommand } from 'Store/Actions/commandActions';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import { asStateRecord, type LegacyDispatch } from 'Store/StateTypes';
import { isCommandExecuting } from 'Utilities/Command';
import BookSearchCell from './BookSearchCell';

function createMapStateToProps() {
  return createSelector(
    (_state: unknown, { bookId }: { bookId: BookId }) => bookId,
    createCommandsSelector(),
    (bookId, commands) => ({
      isSearching: commands.some((command) => {
        const ids = asStateRecord(command.body).bookIds;
        return command.name === commandNames.BOOK_SEARCH && isCommandExecuting(command) &&
          Array.isArray(ids) && ids.includes(bookId);
      })
    })
  );
}

function createMapDispatchToProps(dispatch: LegacyDispatch, props: { bookId: BookId }) {
  return {
    onSearchPress() {
      dispatch(executeCommand({ name: commandNames.BOOK_SEARCH, bookIds: [props.bookId] }));
    }
  };
}

export default connect(createMapStateToProps, createMapDispatchToProps)(BookSearchCell);
