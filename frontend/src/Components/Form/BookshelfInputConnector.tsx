import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { clearOptions, defaultState, fetchOptions } from 'Store/Actions/providerOptionActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import BookshelfInput from './BookshelfInput';
import { type ProviderControlProps,providerFields, providerItems, providerKeys } from './providerControlTypes';

type OwnProps = ProviderControlProps;

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => asStateRecord(state.providerOptions.bookshelves ?? defaultState),
    (_state: LegacyRootState, props: OwnProps) => props.name,
    (bookshelves, name) => {
      const items = asStateRecord(bookshelves.items);
      const helptext = asStateRecord(items.helptext)[name];

      return {
        helptext: typeof helptext === 'string' ? helptext : '',
        user: typeof items.user === 'string' ? items.user : '',
        items: providerItems(items.shelves),
        isFetching: bookshelves.isFetching === true,
        isPopulated: bookshelves.isPopulated === true
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchOptions: fetchOptions,
  dispatchClearOptions: clearOptions
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class BookshelfInputConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount = () => {
    if (this._getAccessToken(this.props)) {
      this._populate();
    }
  };

  componentDidUpdate(prevProps: Props) {
    const newToken = this._getAccessToken(this.props);
    const oldToken = this._getAccessToken(prevProps);
    if (newToken && newToken !== oldToken) {
      this._populate();
    }
  }

  componentWillUnmount = () => {
    this.props.dispatchClearOptions({ section: 'bookshelves' });
  };

  //
  // Control

  _populate() {
    const {
      provider,
      providerData,
      dispatchFetchOptions,
      name
    } = this.props;

    dispatchFetchOptions({
      section: 'bookshelves',
      action: 'getBookshelves',
      queryParams: { name },
      provider,
      providerData
    });
  }

  _getAccessToken(props: Props) {
    return providerFields(props.providerData).find((field) => field.name === 'accessToken')?.value;
  }

  //
  // Render

  render() {
    return (
      <BookshelfInput
        {...this.props}
        value={providerKeys(this.props.value)}
      />
    );
  }
}

export default connector(BookshelfInputConnector);
