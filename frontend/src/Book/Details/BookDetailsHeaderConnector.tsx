import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { BookId } from 'Book/BookIdentity';
import type { LibraryEdition } from 'Book/LibraryModels';
import { toggleBooksMonitored } from 'Store/Actions/bookActions';
import createBookSelector from 'Store/Selectors/createBookSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import type { ResourceCollection } from 'Store/Selectors/selectById';
import { asStateRecord } from 'Store/StateTypes';
import BookDetailsHeader from './BookDetailsHeader';
import { authorReference, bookHeaderData, optionalText } from './detailPresentation';

interface OwnProps {
  bookId: BookId;
  author: unknown;
  width: number;
}

function createMapStateToProps() {
  return createSelector(
    createBookSelector(),
    (state: { editions: ResourceCollection<LibraryEdition> }) => state.editions.items,
    (_state: unknown, props: OwnProps) => props.author,
    createUISettingsSelector(),
    createDimensionsSelector(),
    (book, editions, rawAuthor, uiSettings, dimensions) => {
      const edition = editions.find((item) => item.bookId === book?.id && item.monitored);
      const data = bookHeaderData(book);
      return {
        ...data,
        overview: optionalText(asStateRecord(edition).overview) || data.overview,
        author: authorReference(rawAuthor),
        shortDateFormat: uiSettings.shortDateFormat,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const connector = connect(createMapStateToProps, { toggleBooksMonitored });
type Props = OwnProps & ConnectedProps<typeof connector>;

class BookDetailsHeaderConnector extends Component<Props> {
  onMonitorTogglePress = (monitored: boolean) => {
    this.props.toggleBooksMonitored({ bookIds: [this.props.bookId], monitored });
  };

  render() {
    return <BookDetailsHeader {...this.props} onMonitorTogglePress={this.onMonitorTogglePress} />;
  }
}

export default connector(BookDetailsHeaderConnector);
