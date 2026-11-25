import classNames from 'classnames';
import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
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
import formControlProps from './formControlProps';
import HintedSelectInputOption from './HintedSelectInputOption';
import HintedSelectInputSelectedValue from './HintedSelectInputSelectedValue';
import { nextEnabledOptionIndex, selectedOptionIndex } from './selectInputSelection';
import TextInput from './TextInput';
import styles from './EnhancedSelectInput.css';

function isArrowKey(keyCode) {
  return keyCode === keyCodes.UP_ARROW || keyCode === keyCodes.DOWN_ARROW;
}

function getSelectedOption(selectedIndex, values) {
  return values[selectedIndex];
}

function previousIndex(selectedIndex, values) {
  return nextEnabledOptionIndex(selectedIndex, -1, values);
}

function nextIndex(selectedIndex, values) {
  return nextEnabledOptionIndex(selectedIndex, 1, values);
}

function getSelectedIndex({ value, values }) {
  return selectedOptionIndex(value, values);
}

function isSelectedItem(index, props) {
  const {
    value,
    values
  } = props;

  if (Array.isArray(value)) {
    return value.includes(values[index].key);
  }

  return values[index].key === value;
}

function getKey(selectedIndex, values) {
  return values[selectedIndex].key;
}

class EnhancedSelectInput extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this._buttonId = getUniqueElementId();
    this._optionsId = getUniqueElementId();
    this._valueId = getUniqueElementId();

    this.state = {
      isOpen: false,
      selectedIndex: getSelectedIndex(props),
      isMobile: isMobileUtil()
    };
  }

  componentDidUpdate(prevProps, prevState) {
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

  //
  // Control

  setReference = (reference) => {
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

  onWindowClick = (event) => {
    const button = document.getElementById(this._buttonId);
    const options = document.getElementById(this._optionsId);

    if (!button || !event.target.isConnected || this.state.isMobile) {
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

  onKeyDown = (event) => {
    const {
      values
    } = this.props;

    const {
      isOpen,
      selectedIndex
    } = this.state;

    const keyCode = event.keyCode;
    const newState = {};

    if (!isOpen) {
      if (isArrowKey(keyCode)) {
        event.preventDefault();
        newState.isOpen = true;
        this.props.onOpen?.();
      }

      if (
        selectedIndex == null || selectedIndex === -1 ||
        !getSelectedOption(selectedIndex, values) ||
        getSelectedOption(selectedIndex, values).isDisabled
      ) {
        if (keyCode === keyCodes.UP_ARROW) {
          newState.selectedIndex = previousIndex(0, values);
        } else if (keyCode === keyCodes.DOWN_ARROW) {
          newState.selectedIndex = nextIndex(values.length - 1, values);
        }
      }

      this.setState(newState);
      return;
    }

    if (keyCode === keyCodes.UP_ARROW) {
      event.preventDefault();
      newState.selectedIndex = previousIndex(selectedIndex, values);
    }

    if (keyCode === keyCodes.DOWN_ARROW) {
      event.preventDefault();
      newState.selectedIndex = nextIndex(selectedIndex, values);
    }

    if (keyCode === keyCodes.ENTER) {
      event.preventDefault();
      newState.isOpen = false;
      if (getSelectedOption(selectedIndex, values) && !getSelectedOption(selectedIndex, values).isDisabled) {
        this.onSelect(getKey(selectedIndex, values));
      }
    }

    if (keyCode === keyCodes.TAB) {
      newState.isOpen = false;
      if (getSelectedOption(selectedIndex, values) && !getSelectedOption(selectedIndex, values).isDisabled) {
        this.onSelect(getKey(selectedIndex, values));
      }
    }

    if (keyCode === keyCodes.ESCAPE) {
      event.preventDefault();
      event.stopPropagation();
      newState.isOpen = false;
      newState.selectedIndex = getSelectedIndex(this.props);
    }

    if (!_.isEmpty(newState)) {
      this.setState(newState);
    }
  };

  onPress = () => {
    if (!this.state.isOpen && this.props.onOpen) {
      this.props.onOpen();
    }

    this.setState({ isOpen: !this.state.isOpen });
  };

  onSelect = (value) => {
    if (Array.isArray(this.props.value)) {
      let newValue = null;
      const index = this.props.value.indexOf(value);
      if (index === -1) {
        newValue = this.props.values.map((v) => v.key).filter((v) => (v === value) || this.props.value.includes(v));
      } else {
        newValue = [...this.props.value];
        newValue.splice(index, 1);
      }
      this.props.onChange({
        name: this.props.name,
        value: newValue
      });
    } else {
      this.setState({ isOpen: false });

      this.props.onChange({
        name: this.props.name,
        value
      });
    }
  };

  onOptionsModalClose = () => {
    this.setState({ isOpen: false });
  };

  //
  // Render

  render() {
    const {
      className,
      disabledClassName,
      name,
      value,
      values,
      isDisabled,
      isEditable,
      isFetching,
      hasError,
      hasWarning,
      valueOptions,
      selectedValueOptions,
      selectedValueComponent: SelectedValueComponent,
      optionComponent: OptionComponent,
      onChange
    } = this.props;

    const {
      selectedIndex,
      isOpen,
      isMobile
    } = this.state;

    const isMultiSelect = Array.isArray(value);
    const selectedOption = getSelectedOption(selectedIndex, values);
    let selectedValue = value;

    if (!values.length) {
      selectedValue = isMultiSelect ? [] : '';
    }

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
              isEditable ?
                <div
                  className={styles.editableContainer}
                >
                  <TextInput
                    {...controlProps}
                    className={className}
                    name={name}
                    value={value}
                    readOnly={isDisabled}
                    hasError={hasError}
                    hasWarning={hasWarning}
                    onFocus={this.onFocus}
                    onBlur={this.onBlur}
                    onChange={onChange}
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
                    value={selectedValue}
                    values={values}
                    {...selectedValueOptions}
                    {...(isMultiSelect ? {} : selectedOption)}
                    id={this._valueId}
                    isDisabled={isDisabled}
                    isMultiSelect={isMultiSelect}
                  >
                    {selectedOption ? selectedOption.value : null}
                  </SelectedValueComponent>

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
                      values.map((v, index) => {
                        const hasParent = v.parentKey !== undefined;
                        const depth = hasParent ? 1 : 0;
                        const parentSelected = hasParent && Array.isArray(value) && value.includes(v.parentKey);

                        return (
                          <OptionComponent
                            key={v.key}
                            id={v.key}
                            depth={depth}
                            isSelected={isSelectedItem(index, this.props)}
                            isDisabled={parentSelected}
                            isMultiSelect={isMultiSelect}
                            {...valueOptions}
                            {...v}
                            isMobile={false}
                            onSelect={this.onSelect}
                          >
                            {v.value}
                          </OptionComponent>
                        );
                      })
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
                innerClassName={styles.optionsInnerModalBody}
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
                    values.map((v, index) => {
                      const hasParent = v.parentKey !== undefined;
                      const depth = hasParent ? 1 : 0;
                      const parentSelected = hasParent && Array.isArray(value) && value.includes(v.parentKey);
                      return (
                        <OptionComponent
                          key={v.key}
                          id={v.key}
                          depth={depth}
                          isSelected={isSelectedItem(index, this.props)}
                          isMultiSelect={isMultiSelect}
                          isDisabled={parentSelected}
                          {...valueOptions}
                          {...v}
                          isMobile={true}
                          onSelect={this.onSelect}
                        >
                          {v.value}
                        </OptionComponent>
                      );
                    })
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

EnhancedSelectInput.propTypes = {
  className: PropTypes.string,
  disabledClassName: PropTypes.string,
  name: PropTypes.string.isRequired,
  value: PropTypes.oneOfType([PropTypes.string, PropTypes.number, PropTypes.arrayOf(PropTypes.string), PropTypes.arrayOf(PropTypes.number)]).isRequired,
  values: PropTypes.arrayOf(PropTypes.object).isRequired,
  isDisabled: PropTypes.bool.isRequired,
  isFetching: PropTypes.bool.isRequired,
  isEditable: PropTypes.bool.isRequired,
  hasError: PropTypes.bool,
  hasWarning: PropTypes.bool,
  valueOptions: PropTypes.object.isRequired,
  selectedValueOptions: PropTypes.object.isRequired,
  selectedValueComponent: PropTypes.oneOfType([PropTypes.string, PropTypes.func]).isRequired,
  optionComponent: PropTypes.elementType,
  onOpen: PropTypes.func,
  onChange: PropTypes.func.isRequired
};

EnhancedSelectInput.defaultProps = {
  className: styles.enhancedSelect,
  disabledClassName: styles.isDisabled,
  isDisabled: false,
  isFetching: false,
  isEditable: false,
  valueOptions: {},
  selectedValueOptions: {},
  selectedValueComponent: HintedSelectInputSelectedValue,
  optionComponent: HintedSelectInputOption
};

export default EnhancedSelectInput;
