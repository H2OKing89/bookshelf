import React, { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { LegacyRootState } from 'Store/StateTypes';
import type { ValueChange } from './formTypes';
import { getProfileDefault, parseProfileValue, profileOptions, profiles, type ProfileSelectProps } from './profileSelectOptions';
import SelectInput from './SelectInput';

type OwnProps = ProfileSelectProps;

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.qualityProfiles,
    (_state: LegacyRootState, props: OwnProps) => props.includeNoChange,
    (_state: LegacyRootState, props: OwnProps) => props.includeNoChangeDisabled,
    (_state: LegacyRootState, props: OwnProps) => props.includeMixed,
    (section, includeNoChange, includeNoChangeDisabled, includeMixed) => {
      const items = profiles(section);
      return { values: profileOptions(items, includeNoChange, includeNoChangeDisabled, includeMixed) };
    }
  );
}

const connector = connect(createMapStateToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class QualityProfileSelectInputConnector extends Component<Props> {
  componentDidMount() {
    const { name, value, values } = this.props;
    const defaultValue = getProfileDefault(value, values);

    if (defaultValue !== undefined) {
      this.props.onChange({ name, value: defaultValue });
    }
  }

  onChange = ({ name, value }: ValueChange<string>) => {
    const parsed = parseProfileValue(value);
    if (parsed !== undefined) {
      this.props.onChange({ name, value: parsed });
    }
  };

  render() {
    return <SelectInput {...this.props} onChange={this.onChange} />;
  }
}

export default connector(QualityProfileSelectInputConnector);
