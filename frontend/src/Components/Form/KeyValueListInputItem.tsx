import { Component } from 'react';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import type { KeyValueItem } from './KeyValueListInput';
import TextInput, { type TextInputChange } from './TextInput';
import styles from './KeyValueListInputItem.css';

interface KeyValueListInputItemProps {
  id?: string;
  index: number;
  keyValue: string;
  value: string | number;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
  isNew: boolean;
  onChange: (index: number, value: KeyValueItem) => void;
  onRemove: (index: number) => void;
  onFocus: () => void;
  onBlur: () => void;
}

class KeyValueListInputItem extends Component<KeyValueListInputItemProps> {

  //
  // Listeners

  onKeyChange = ({ value: keyValue }: TextInputChange) => {
    const {
      index,
      value,
      onChange
    } = this.props;

    onChange(index, { key: keyValue, value });
  };

  onValueChange = ({ value }: TextInputChange) => {
    // TODO: Validate here or validate at a lower level component

    const {
      index,
      keyValue,
      onChange
    } = this.props;

    onChange(index, { key: keyValue, value });
  };

  onRemovePress = () => {
    const {
      index,
      onRemove
    } = this.props;

    onRemove(index);
  };

  onFocus = () => {
    this.props.onFocus();
  };

  onBlur = () => {
    this.props.onBlur();
  };

  //
  // Render

  render() {
    const {
      id,
      keyValue,
      value,
      keyPlaceholder = 'Key',
      valuePlaceholder = 'Value',
      isNew
    } = this.props;

    return (
      <div className={styles.itemContainer}>
        <div className={styles.inputWrapper}>
          <TextInput
            id={id}
            aria-label={keyPlaceholder}
            className={styles.keyInput}
            name="key"
            value={keyValue}
            placeholder={keyPlaceholder}
            onChange={this.onKeyChange}
            onFocus={this.onFocus}
            onBlur={this.onBlur}
          />
        </div>

        <div className={styles.inputWrapper}>
          <TextInput
            aria-label={valuePlaceholder}
            className={styles.valueInput}
            name="value"
            value={value}
            placeholder={valuePlaceholder}
            onChange={this.onValueChange}
            onFocus={this.onFocus}
            onBlur={this.onBlur}
          />
        </div>

        <div className={styles.buttonWrapper}>
          {
            isNew ?
              null :
              <IconButton
                name={icons.REMOVE}
                tabIndex={-1}
                aria-label={translate('RemoveEntry')}
                onPress={this.onRemovePress}
              />
          }
        </div>
      </div>
    );
  }
}

export default KeyValueListInputItem;
