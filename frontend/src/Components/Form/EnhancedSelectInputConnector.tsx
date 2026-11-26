import _ from 'lodash';
import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { clearOptions, defaultState, fetchOptions } from 'Store/Actions/providerOptionActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import EnhancedSelectInput from './EnhancedSelectInput';
import { isProviderKey, type ProviderControlProps, providerFields, type ProviderKey } from './providerControlTypes';

interface OwnProps extends ProviderControlProps {
  value?: ProviderKey | ProviderKey[] | null;
  selectOptionsProviderAction?: string;
}

const importantFieldNames = [
  'baseUrl',
  'apiPath',
  'apiKey'
];

function getProviderDataKey(providerData: unknown) {
  const fields = providerFields(providerData)
    .filter((f) => typeof f.name === 'string' && importantFieldNames.includes(f.name))
    .map((f) => f.value);

  return fields;
}

export function getSelectOptions(items: unknown) {
  if (!Array.isArray(items)) {
    return [];
  }

  return items.flatMap((item: unknown) => {
    const option = asStateRecord(item);
    if (!isProviderKey(option.value) || typeof option.name !== 'string') {
      return [];
    }

    return [{
      key: option.value,
      value: option.name,
      hint: typeof option.hint === 'string' ? option.hint : undefined,
      parentKey: isProviderKey(option.parentValue) ? option.parentValue : undefined
    }];
  });
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState, { selectOptionsProviderAction }: OwnProps) =>
      asStateRecord(state.providerOptions[selectOptionsProviderAction ?? ''] ?? defaultState),
    (options) => {
      return {
        isFetching: options.isFetching === true,
        values: getSelectOptions(options.items)
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchOptions: fetchOptions,
  dispatchClearOptions: clearOptions as unknown as (payload: { section: string }) => { type: string; payload?: unknown }
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class EnhancedSelectInputConnector extends Component<Props, { refetchRequired: boolean }> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      refetchRequired: false
    };
  }

  componentDidMount = () => {
    this._populate();
  };

  componentDidUpdate = (prevProps: Props) => {
    const prevKey = getProviderDataKey(prevProps.providerData);
    const nextKey = getProviderDataKey(this.props.providerData);

    if (!_.isEqual(prevKey, nextKey)) {
      this.setState({ refetchRequired: true });
    }
  };

  componentWillUnmount = () => {
    this._cleanup();
  };

  //
  // Listeners

  onOpen = () => {
    if (this.state.refetchRequired) {
      this._populate();
    }
  };

  //
  // Control

  _populate() {
    const {
      provider,
      providerData,
      selectOptionsProviderAction,
      dispatchFetchOptions
    } = this.props;

    if (selectOptionsProviderAction) {
      this.setState({ refetchRequired: false });
      dispatchFetchOptions({
        section: selectOptionsProviderAction,
        action: selectOptionsProviderAction,
        provider,
        providerData
      });
    }
  }

  _cleanup() {
    const {
      selectOptionsProviderAction,
      dispatchClearOptions
    } = this.props;

    if (selectOptionsProviderAction) {
      dispatchClearOptions({ section: selectOptionsProviderAction });
    }
  }

  //
  // Render

  render() {
    return (
      <EnhancedSelectInput
        {...this.props}
        onOpen={this.onOpen}
      />
    );
  }
}

export default connector(EnhancedSelectInputConnector);
