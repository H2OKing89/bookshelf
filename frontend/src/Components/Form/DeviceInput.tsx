import { Component } from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import formControlProps, { type FormControlProps } from './formControlProps';
import FormInputButton from './FormInputButton';
import type { ValueChange } from './formTypes';
import type { ProviderItem, ProviderKey } from './providerControlTypes';
import TagInput from './TagInput';
import styles from './DeviceInput.css';

interface Props extends FormControlProps {
  className?: string;
  name: string;
  value: readonly ProviderKey[];
  items: readonly ProviderItem[];
  hasError?: boolean;
  hasWarning?: boolean;
  isFetching: boolean;
  isPopulated: boolean;
  onChange: (change: ValueChange<ProviderKey[]>) => void;
  selectedDevices: readonly ProviderItem[];
  onRefreshPress: () => void;
}

class DeviceInput extends Component<Props> {
  static defaultProps = { className: styles.deviceInputWrapper };

  onTagAdd = (device: { id?: ProviderKey; name: string }) => {
    const {
      name,
      value,
      onChange
    } = this.props;

    // New tags won't have an ID, only a name.
    const deviceId = device.id ?? device.name;

    onChange({
      name,
      value: [...value, deviceId]
    });
  };

  onTagDelete = ({ index }: { index: number }) => {
    const {
      name,
      value,
      onChange
    } = this.props;

    const newValue = value.slice();
    newValue.splice(index, 1);

    onChange({
      name,
      value: newValue
    });
  };

  //
  // Render

  render() {
    const {
      className,
      name,
      items,
      selectedDevices,
      hasError,
      hasWarning,
      isFetching,
      onRefreshPress
    } = this.props;

    return (
      <div className={className}>
        <TagInput
          {...formControlProps(this.props)}
          inputContainerClassName={styles.input}
          name={name}
          tags={selectedDevices}
          tagList={items}
          allowNew={true}
          minQueryLength={0}
          hasError={hasError}
          hasWarning={hasWarning}
          onTagAdd={this.onTagAdd}
          onTagDelete={this.onTagDelete}
        />

        <FormInputButton
          onPress={onRefreshPress}
        >
          <Icon
            name={icons.REFRESH}
            isSpinning={isFetching}
          />
        </FormInputButton>
      </div>
    );
  }
}

export default DeviceInput;
