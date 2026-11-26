import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { clearOptions, defaultState, fetchOptions } from 'Store/Actions/providerOptionActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import DeviceInput from './DeviceInput';
import { type ProviderControlProps,providerItems, providerKeys } from './providerControlTypes';

type OwnProps = ProviderControlProps;

function createMapStateToProps() {
  return createSelector(
    (_state: LegacyRootState, { value }: OwnProps) => value,
    (state: LegacyRootState) => asStateRecord(state.providerOptions.devices ?? defaultState),
    (value, devices) => {

      const items = providerItems(devices.items);

      return {
        items,
        isFetching: devices.isFetching === true,
        isPopulated: devices.isPopulated === true,
        selectedDevices: providerKeys(value).map((valueDevice) => {
          const device = items.find((item) => String(item.id) === String(valueDevice));

          if (device) {
            return {
              id: device.id,
              name: `${device.name} (${device.id})`
            };
          }

          return {
            id: valueDevice,
            name: `Unknown (${valueDevice})`
          };
        })
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

class DeviceInputConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount = () => {
    this._populate();
  };

  componentWillUnmount = () => {
    this.props.dispatchClearOptions({ section: 'devices' });
  };

  //
  // Control

  _populate() {
    const {
      provider,
      providerData,
      dispatchFetchOptions
    } = this.props;

    dispatchFetchOptions({
      section: 'devices',
      action: 'getDevices',
      provider,
      providerData
    });
  }

  //
  // Listeners

  onRefreshPress = () => {
    this._populate();
  };

  //
  // Render

  render() {
    return (
      <DeviceInput
        {...this.props}
        value={providerKeys(this.props.value)}
        onRefreshPress={this.onRefreshPress}
      />
    );
  }
}

export default connector(DeviceInputConnector);
