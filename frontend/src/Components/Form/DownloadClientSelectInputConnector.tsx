import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { fetchDownloadClients } from 'Store/Actions/settingsActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import EnhancedSelectInput from './EnhancedSelectInput';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import { numericChoice, providerSelectOptions } from './providerSelectOptions';

interface OwnProps extends FormControlProps {
  name: string;
  value?: number | string | null;
  includeAny?: boolean;
  className?: string;
  hasError?: boolean;
  hasWarning?: boolean;
  isDisabled?: boolean;
  onChange: (change: ValueChange<number>) => void;
  protocol?: string;
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.downloadClients,
    (_state: LegacyRootState, props: OwnProps) => props.includeAny,
    (_state: LegacyRootState, props: OwnProps) => props.protocol ?? 'torrent',
    (value, includeAny, protocol) => {
      const section = asStateRecord(value);
      return {
        isFetching: section.isFetching === true,
        isPopulated: section.isPopulated === true,
        error: section.error,
        values: providerSelectOptions(section, includeAny, protocol)
      };
    }
  );
}

const connector = connect(createMapStateToProps, {
  dispatchFetchDownloadClients: fetchDownloadClients
});
type Props = OwnProps & ConnectedProps<typeof connector>;

class DownloadClientSelectInputConnector extends Component<Props> {
  componentDidMount() {
    if (!this.props.isPopulated) {
      this.props.dispatchFetchDownloadClients();
    }
  }

  render() {
    return (
      <EnhancedSelectInput<number>
        {...this.props}
        value={numericChoice(this.props.value)}
      />
    );
  }
}

export default connector(DownloadClientSelectInputConnector);
