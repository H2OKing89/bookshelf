import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { metadataProfileNames } from 'Helpers/Props';
import type { LegacyRootState } from 'Store/StateTypes';
import type { ValueChange } from './formTypes';
import { getProfileDefault, parseProfileValue, profileOptions, profiles, type ProfileSelectProps } from './profileSelectOptions';
import SelectInput from './SelectInput';

type OwnProps = ProfileSelectProps & { includeNone?: boolean };

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.metadataProfiles,
    (_state: LegacyRootState, props: OwnProps) => props.includeNoChange,
    (_state: LegacyRootState, props: OwnProps) => props.includeNoChangeDisabled,
    (_state: LegacyRootState, props: OwnProps) => props.includeMixed,
    (_state: LegacyRootState, props: OwnProps) => props.includeNone ?? true,
    (section, includeNoChange, includeNoChangeDisabled, includeMixed, includeNone) => {
      const items = profiles(section);
      const none = items.find((item) => item.name === metadataProfileNames.NONE);
      const visible = items.filter((item) => item.name !== metadataProfileNames.NONE);
      if (includeNone && none) {
        visible.push(none);
      }
      return { values: profileOptions(visible, includeNoChange, includeNoChangeDisabled, includeMixed) };
    }
  );
}

const connector = connect(createMapStateToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class MetadataProfileSelectInputConnector extends Component<Props> {
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

export default connector(MetadataProfileSelectInputConnector);
