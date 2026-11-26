import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { fetchIndexers } from 'Store/Actions/settingsActions';
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
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.indexers,
    (_state: LegacyRootState, props: OwnProps) => props.includeAny,
    (value, includeAny) => {
      const section = asStateRecord(value);
      return {
        isFetching: section.isFetching === true,
        isPopulated: section.isPopulated === true,
        error: section.error,
        values: providerSelectOptions(section, includeAny)
      };
    }
  );
}

const connector = connect(createMapStateToProps, {
  dispatchFetchIndexers: fetchIndexers
});
type Props = OwnProps & ConnectedProps<typeof connector>;

class IndexerSelectInputConnector extends Component<Props> {
  componentDidMount() {
    if (!this.props.isPopulated) {
      this.props.dispatchFetchIndexers();
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

export default connector(IndexerSelectInputConnector);
