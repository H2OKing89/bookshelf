import classNames from 'classnames';
import { Component, type KeyboardEvent } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import Portal from 'Components/Portal';
import AnchoredPopup, { popupMaxHeight } from 'Components/Positioning/AnchoredPopup';
import Scroller from 'Components/Scroller/Scroller';
import { icons, scrollDirections, sizes } from 'Helpers/Props';
import { isMobile as isMobileUtil } from 'Utilities/browser';
import * as keyCodes from 'Utilities/Constants/keyCodes';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import translate from 'Utilities/String/translate';
import { type EnhancedOption, type EnhancedSelectInputProps, isMultiSelect, isOptionDisabled, optionLabel, type SelectKey,toggleSelectedKey } from './enhancedSelectTypes';
import formControlProps from './formControlProps';
import HintedSelectInputOption from './HintedSelectInputOption';
import HintedSelectInputSelectedValue from './HintedSelectInputSelectedValue';
import { nextEnabledOptionIndex, selectedOptionIndex } from './selectInputSelection';
import TextInput from './TextInput';
import styles from './EnhancedSelectInput.css';

interface State {
  isOpen: boolean;
  selectedIndex: number;
  isMobile: boolean;
}

function getSelectedIndex<Key extends SelectKey>({ value, values }: EnhancedSelectInputProps<Key>): number {
  return selectedOptionIndex(value, values);
}

class EnhancedSelectInput<Key extends SelectKey> extends Component<EnhancedSelectInputProps<Key>, State> {

  //
  // Lifecycle

  constructor(props: EnhancedSelectInputProps<Key>) {
    super(props);

    this.state = {
      isOpen: false,
      selectedIndex: getSelectedIndex(props),
      isMobile: isMobileUtil()
    };
  }

  componentDidUpdate(prevProps: EnhancedSelectInputProps<Key>, prevState: State) {
    if (!prevState.isOpen && this.state.isOpen && !this.state.isMobile) {
      this._addListener();
    } else if (prevState.isOpen && !this.state.isOpen) {
      this._removeListener();
    }

    if (!Array.isArray(this.props.value)) {
      if (prevProps.value !== this.props.value || prevProps.values !== this.props.values) {
        const selectedIndex = getSelectedIndex(this.props);
        if (selectedIndex !== this.state.selectedIndex) {
          this.setState({ selectedIndex });
        }
      }
    }
  }

  componentWillUnmount() {
    this._removeListener();
  }

  private readonly _buttonId = getUniqueElementId();
  private readonly _optionsId = getUniqueElementId();
  private readonly _valueId = getUniqueElementId();
  private _reference: HTMLElement | null = null;

  //
  // Control

  setReference = (reference: HTMLElement | null) => {
    this._reference = reference;
  };

  _addListener() {
    window.addEventListener('click', this.onWindowClick);
  }

  _removeListener() {
    window.removeEventListener('click', this.onWindowClick);
  }

  //
  // Listeners

  onWindowClick = (event: MouseEvent) => {
    const button = document.getElementById(this._buttonId);
    const options = document.getElementById(this._optionsId);

    if (!button || !(event.target instanceof Node) || !event.target.isConnected || this.state.isMobile) {
      return;
    }

    if (
      !button.contains(event.target) &&
      options &&
      !options.contains(event.target) &&
      this.state.isOpen
    ) {
      this.setState({ isOpen: false });
    }
  };

  onFocus = () => {
    if (this.state.isOpen) {
      this.setState({ isOpen: false });
    }
  };

  onBlur = () => {
    if (!this.props.isEditable) {
      // Calling setState without this check prevents the click event from being properly handled on Chrome (it is on firefox)
      const origIndex = getSelectedIndex(this.props);

      if (origIndex !== this.state.selectedIndex) {
        this.setState({ selectedIndex: origIndex });
      }
    }
  };

  onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    const { values, value, isDisabled } = this.props;
    const { isOpen, selectedIndex } = this.state;
    if (isDisabled) {
      return;
    }

    const keyCode = event.keyCode;
    const options = values.map((option) => ({ ...option, isDisabled: isOptionDisabled(option, value) }));
    const selected = options[selectedIndex];
    const arrow = keyCode === keyCodes.UP_ARROW || keyCode === keyCodes.DOWN_ARROW;

    if (!isOpen) {
      if (arrow) {
        event.preventDefault();
        this.props.onOpen?.();
        this.setState({
          isOpen: true,
          selectedIndex: !selected || selected.isDisabled ?
            nextEnabledOptionIndex(-1, keyCode === keyCodes.UP_ARROW ? -1 : 1, options) : selectedIndex
        });
      }
      return;
    }

    if (arrow) {
      event.preventDefault();
      this.setState({ selectedIndex: nextEnabledOptionIndex(selectedIndex, keyCode === keyCodes.UP_ARROW ? -1 : 1, options) });
    } else if (keyCode === keyCodes.ENTER || keyCode === keyCodes.TAB) {
      if (keyCode === keyCodes.ENTER) {
        event.preventDefault();
      }

      this.setState({ isOpen: false });
      if (selected && !selected.isDisabled && !(keyCode === keyCodes.TAB && event.shiftKey)) {
        this.onSelect(selected.key);
      }
    } else if (keyCode === keyCodes.ESCAPE) {
      event.preventDefault();
      event.stopPropagation();
      this.setState({ isOpen: false, selectedIndex: getSelectedIndex(this.props) });
    }
  };

  onPress = () => {
    if (this.props.isDisabled) {
      return;
    }

    if (!this.state.isOpen) {
      this.props.onOpen?.();
    }

    this.setState((state) => ({ isOpen: !state.isOpen }));
  };

  onSelect = (key: SelectKey) => {
    const option = this.props.values.find((candidate) => candidate.key === key);
    if (this.props.isDisabled || !option || isOptionDisabled(option, this.props.value)) {
      return;
    }

    const props = this.props;
    if (isMultiSelect<Key>(props)) {
      props.onChange({ name: props.name, value: toggleSelectedKey(props.value, props.values, option.key) });
    } else {
      this.setState({ isOpen: false });
      props.onChange({ name: props.name, value: option.key });
    }
  };

  onOptionsModalClose = () => {
    this.setState({ isOpen: false });
  };

  //
  // Render

  render() {
    const {
      className = styles.enhancedSelect,
      disabledClassName = styles.isDisabled,
      name,
      value,
      values,
      isDisabled = false,
      isEditable = false,
      isFetching = false,
      hasError,
      hasWarning,
      valueOptions = {},
      selectedValueOptions = {},
      selectedValueComponent: SelectedValueComponent = HintedSelectInputSelectedValue,
      optionComponent: OptionComponent = HintedSelectInputOption,
      onEditableChange
    } = this.props;

    const {
      selectedIndex,
      isOpen,
      isMobile
    } = this.state;

    const multiple = isMultiSelect<Key>(this.props);
    const selectedOption = values[selectedIndex];
    const selectedValue = !multiple && selectedOption ? optionLabel(selectedOption.value) : value;
    const renderOption = (option: EnhancedOption<Key>, mobile: boolean) => {
      const selected = multiple && Array.isArray(value) ? value.includes(option.key) : value === option.key;
      const { key: optionKey, ...optionProps } = option;
      const label = optionLabel(option.value);
      return (
        <OptionComponent
          key={`${typeof optionKey}:${optionKey}`}
          {...valueOptions}
          {...optionProps}
          id={optionKey}
          value={label}
          depth={option.depth ?? (option.parentKey === undefined ? 0 : 1)}
          isSelected={selected}
          isDisabled={isOptionDisabled(option, value)}
          isMultiSelect={multiple}
          isMobile={mobile}
          onSelect={this.onSelect}
        >
          {label}
        </OptionComponent>
      );
    };

    const controlProps = formControlProps(this.props);
    const labelledBy = controlProps['aria-labelledby'];

    return (
      <div>
        <div
          ref={this.setReference}
          id={this._buttonId}
        >
          <div>
            {
              isEditable && !multiple ?
                <div
                  className={styles.editableContainer}
                >
                  <TextInput
                    {...controlProps}
                    className={className}
                    name={name}
                    value={typeof value === 'string' || typeof value === 'number' ? value : null}
                    readOnly={isDisabled}
                    hasError={hasError}
                    hasWarning={hasWarning}
                    onFocus={this.onFocus}
                    onBlur={this.onBlur}
                    onChange={(change) => onEditableChange?.(change)}
                  />
                  <Link
                    aria-label={translate('Options')}
                    aria-expanded={isOpen}
                  aria-controls={isOpen && !isMobile ? this._optionsId : undefined}
                    className={classNames(
                      styles.dropdownArrowContainerEditable,
                      isDisabled ?
                        styles.dropdownArrowContainerDisabled :
                        styles.dropdownArrowContainer)
                    }
                    isDisabled={isDisabled}
                    onPress={this.onPress}
                  >
                    {
                      isFetching ?
                        <LoadingIndicator
                          className={styles.loading}
                          size={20}
                        /> :
                        null
                    }

                    {
                      isFetching ?
                        null :
                        <Icon
                          name={icons.CARET_DOWN}
                        />
                    }
                  </Link>
                </div> :
                <Link
                  {...controlProps}
                  aria-labelledby={labelledBy ? `${labelledBy} ${this._valueId}` : undefined}
                  aria-expanded={isOpen}
                  aria-controls={isOpen && !isMobile ? this._optionsId : undefined}
                  className={classNames(
                    className,
                    hasError && styles.hasError,
                    hasWarning && styles.hasWarning,
                    isDisabled && disabledClassName
                  )}
                  isDisabled={isDisabled}
                  onBlur={this.onBlur}
                  onKeyDown={this.onKeyDown}
                  onPress={this.onPress}
                >
                  <SelectedValueComponent
                    {...selectedValueOptions}
                    {...(multiple ? {} : selectedOption)}
                    value={selectedValue}
                    values={values}
                    id={this._valueId}
                    isDisabled={isDisabled}
                    isMultiSelect={multiple}
                  />

                  <div
                    className={isDisabled ?
                      styles.dropdownArrowContainerDisabled :
                      styles.dropdownArrowContainer
                    }
                  >

                    {
                      isFetching ?
                        <LoadingIndicator
                          className={styles.loading}
                          size={20}
                        /> :
                        null
                    }

                    {
                      isFetching ?
                        null :
                        <Icon
                          name={icons.CARET_DOWN}
                        />
                    }
                  </div>
                </Link>
            }
          </div>
        </div>
        {isOpen && !isMobile &&
          <Portal>
            <AnchoredPopup
              reference={this._reference}
              placement="bottom-start"
              fit={true}
              matchWidth="minimum"
              padding={10}
            >
              {({ ref, style }) => (
                <div
                  ref={ref}
                  id={this._optionsId}
                  className={styles.optionsContainer}
                  style={style}
                >
                  <Scroller
                    className={styles.options}
                    style={{ maxHeight: popupMaxHeight }}
                  >
                    {
                      values.map((option) => renderOption(option, false))
                    }
                  </Scroller>
                </div>
              )}
            </AnchoredPopup>
          </Portal>}

        {
          isMobile ?
            <Modal
              aria-label={translate('Options')}
              className={styles.optionsModal}
              size={sizes.EXTRA_SMALL}
              isOpen={isOpen}
              onModalClose={this.onOptionsModalClose}
            >
              <ModalBody
                className={styles.optionsModalBody}
                scrollDirection={scrollDirections.NONE}
              >
                <Scroller className={styles.optionsModalScroller}>
                  <div className={styles.mobileCloseButtonContainer}>
                    <Link
                      className={styles.mobileCloseButton}
                      aria-label={translate('Close')}
                      onPress={this.onOptionsModalClose}
                    >
                      <Icon
                        name={icons.CLOSE}
                        size={18}
                        aria-hidden={true}
                      />
                    </Link>
                  </div>

                  {
                    values.map((option) => renderOption(option, true))
                  }
                </Scroller>
              </ModalBody>
            </Modal> :
            null
        }
      </div>
    );
  }
}

export default EnhancedSelectInput;
