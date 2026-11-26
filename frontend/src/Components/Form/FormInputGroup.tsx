import _ from 'lodash';
import React, { useContext, useState } from 'react';
import Link from 'Components/Link/Link';
import { inputTypes } from 'Helpers/Props';
import { asStateRecord } from 'Store/StateTypes';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import translate from 'Utilities/String/translate';
import AutoCompleteInput from './AutoCompleteInput';
import BookEditionSelectInputConnector from './BookEditionSelectInputConnector';
import BookshelfInputConnector from './BookshelfInputConnector';
import CaptchaInputConnector from './CaptchaInputConnector';
import CheckInput, { type CheckInputProps } from './CheckInput';
import DeviceInputConnector from './DeviceInputConnector';
import DownloadClientSelectInputConnector from './DownloadClientSelectInputConnector';
import EnhancedSelectInput from './EnhancedSelectInput';
import EnhancedSelectInputConnector from './EnhancedSelectInputConnector';
import type { EnhancedOption, SelectKey } from './enhancedSelectTypes';
import type { FormControlProps } from './formControlProps';
import FormGroupContext from './FormGroupContext';
import type { FormInputButtonProps } from './FormInputButton';
import FormInputHelpText from './FormInputHelpText';
import type { FormMessage, ValueChange } from './formTypes';
import IndexerFlagsSelectInput from './IndexerFlagsSelectInput';
import IndexerSelectInputConnector from './IndexerSelectInputConnector';
import KeyValueListInput, { type KeyValueListInputProps } from './KeyValueListInput';
import MetadataProfileSelectInputConnector from './MetadataProfileSelectInputConnector';
import MonitorBooksSelectInput, { type MonitorBooksSelectInputProps } from './MonitorBooksSelectInput';
import MonitorNewItemsSelectInput, { type MonitorNewItemsSelectInputProps } from './MonitorNewItemsSelectInput';
import NumberInput, { type NumberInputProps } from './NumberInput';
import OAuthInputConnector from './OAuthInputConnector';
import PasswordInput from './PasswordInput';
import PathInputConnector from './PathInputConnector';
import { isProviderKey, providerKeys } from './providerControlTypes';
import QualityProfileSelectInputConnector from './QualityProfileSelectInputConnector';
import RootFolderSelectInputConnector from './RootFolderSelectInputConnector';
import TagInputConnector from './TagInputConnector';
import TagSelectInputConnector from './TagSelectInputConnector';
import TextArea, { type TextAreaProps } from './TextArea';
import TextInput, { type TextInputProps } from './TextInput';
import TextTagInputConnector from './TextTagInputConnector';
import UMaskInput from './UMaskInput';
import styles from './FormInputGroup.css';

interface FieldLayoutProps extends FormControlProps {
  className?: string;
  containerClassName?: string;
  inputClassName?: string;
  unit?: string;
  buttons?: React.ReactNode;
  helpText?: string;
  helpTexts?: readonly string[];
  helpTextWarning?: string;
  helpLink?: string;
  pending?: boolean;
  errors?: readonly FormMessage[];
  warnings?: readonly FormMessage[];
}

interface ScalarSelectField {
  type: typeof inputTypes.SELECT;
  name: string;
  value?: SelectKey | null;
  values: readonly EnhancedOption[];
  isDisabled?: boolean;
  isEditable?: boolean;
  selectedValueOptions?: Record<string, unknown>;
  onChange: (change: ValueChange<SelectKey>) => void;
}

interface MultipleSelectField {
  type: typeof inputTypes.SELECT;
  name: string;
  value: SelectKey[];
  values: readonly EnhancedOption[];
  isDisabled?: boolean;
  selectedValueOptions?: Record<string, unknown>;
  onChange: (change: ValueChange<SelectKey[]>) => void;
}

interface TagsField {
  type: typeof inputTypes.TAG | typeof inputTypes.TAG_SELECT;
  name: string;
  value: number[];
  values?: readonly EnhancedOption[];
  isDisabled?: boolean;
  onChange: (change: ValueChange<number[]>) => void;
}

type NativeField =
  | (CheckInputProps & { type: typeof inputTypes.CHECK })
  | (NumberInputProps & { type: typeof inputTypes.NUMBER })
  | (Omit<TextInputProps, 'type'> & { type?: typeof inputTypes.TEXT | typeof inputTypes.FLOAT | typeof inputTypes.PASSWORD })
  | (TextAreaProps & { type: typeof inputTypes.TEXT_AREA })
  | (KeyValueListInputProps & { type: typeof inputTypes.KEY_VALUE_LIST })
  | (MonitorBooksSelectInputProps & { type: typeof inputTypes.MONITOR_BOOKS_SELECT })
  | (MonitorNewItemsSelectInputProps & { type: typeof inputTypes.MONITOR_NEW_ITEMS_SELECT });

type LegacyInputType = Exclude<typeof inputTypes.all[number], NonNullable<NativeField['type']> | typeof inputTypes.SELECT | typeof inputTypes.TAG | typeof inputTypes.TAG_SELECT | typeof inputTypes.INDEXER_FLAGS_SELECT>;

// Provider-backed fields retain unknown schema values and metadata at the
// store boundary. Each typed renderer narrows only the values it displays.
interface LegacyField {
  type: LegacyInputType;
  name: string;
  value?: unknown;
  values?: readonly unknown[];
  onChange: (change: ValueChange<unknown>) => void;
  [key: string]: unknown;
}

interface IndexerFlagsField {
  type: typeof inputTypes.INDEXER_FLAGS_SELECT;
  name: string;
  indexerFlags: number;
  onChange: (change: ValueChange<number>) => void;
}

export type FormInputGroupProps = FieldLayoutProps & (NativeField | ScalarSelectField | MultipleSelectField | TagsField | IndexerFlagsField | LegacyField);

interface ControlPresentation extends FormControlProps {
  className?: string;
  name: string;
  helpText?: string;
  helpTextWarning?: string;
  hasError: boolean;
  hasWarning: boolean;
  hasButton: boolean;
}

function controlInputProps<Props extends FormInputGroupProps>(props: Props) {
  return _.omit(props, [
    'className', 'containerClassName', 'inputClassName', 'type', 'id', 'name',
    'unit', 'buttons', 'helpText', 'helpTexts', 'helpTextWarning', 'helpLink',
    'pending', 'errors', 'warnings', 'aria-describedby', 'aria-invalid',
    'aria-label', 'aria-labelledby'
  ]);
}

function optionalText(value: unknown): string | null | undefined {
  if (value === null) {
    return null;
  }
  return typeof value === 'string' ? value : undefined;
}

function optionalBoolean(value: unknown): boolean | undefined {
  return typeof value === 'boolean' ? value : undefined;
}

function optionalKey(value: unknown): SelectKey | null | undefined {
  if (value === null) {
    return null;
  }
  return isProviderKey(value) ? value : undefined;
}

function renderProviderControl(props: FieldLayoutProps & LegacyField, presentation: ControlPresentation) {
  // These controls consume server-defined field schemas. Keep the raw metadata
  // on the provider request, and narrow only the values presented to controls.
  const input = controlInputProps(props);
  const { value, onChange } = props;
  const provider = typeof props.provider === 'string' ? props.provider : '';
  const providerProps = { provider, providerData: props.providerData };
  const isDisabled = optionalBoolean(props.isDisabled);

  switch (props.type) {
    case inputTypes.AUTO_COMPLETE:
      return <AutoCompleteInput
        {...presentation}
        {...input}
        onChange={onChange}
        value={optionalText(value)}
        values={Array.isArray(props.values) ? props.values.filter((item: unknown): item is string => typeof item === 'string') : []}
        isDisabled={isDisabled}
      />;

    case inputTypes.CAPTCHA:
      return <CaptchaInputConnector {...presentation} {...input} onChange={onChange} {...providerProps} />;

    case inputTypes.DEVICE:
      return <DeviceInputConnector {...presentation} {...input} onChange={onChange} {...providerProps} />;

    case inputTypes.BOOKSHELF:
      return <BookshelfInputConnector {...presentation} {...input} onChange={onChange} {...providerProps} />;

    case inputTypes.OAUTH:
      return <OAuthInputConnector
        {...presentation}
        {...input}
        onChange={onChange}
        {...providerProps}
        label={typeof props.label === 'string' ? props.label : undefined}
        section={typeof props.section === 'string' ? props.section : ''}
      />;

    case inputTypes.PATH:
      return <PathInputConnector
        {...presentation}
        {...input}
        onChange={onChange}
        value={optionalText(value)}
        includeFiles={optionalBoolean(props.includeFiles)}
        hasFileBrowser={optionalBoolean(props.hasFileBrowser)}
      />;

    case inputTypes.QUALITY_PROFILE_SELECT:
      return <QualityProfileSelectInputConnector
        {...presentation}
        {...input}
        onChange={onChange}
        value={optionalKey(value)}
        isDisabled={isDisabled}
        includeNoChange={optionalBoolean(props.includeNoChange)}
        includeNoChangeDisabled={optionalBoolean(props.includeNoChangeDisabled)}
        includeMixed={optionalBoolean(props.includeMixed)}
      />;

    case inputTypes.METADATA_PROFILE_SELECT:
      return <MetadataProfileSelectInputConnector
        {...presentation}
        {...input}
        onChange={onChange}
        value={optionalKey(value)}
        isDisabled={isDisabled}
        includeNone={optionalBoolean(props.includeNone)}
        includeNoChange={optionalBoolean(props.includeNoChange)}
        includeNoChangeDisabled={optionalBoolean(props.includeNoChangeDisabled)}
        includeMixed={optionalBoolean(props.includeMixed)}
      />;

    case inputTypes.BOOK_EDITION_SELECT:
      return <BookEditionSelectInputConnector {...presentation} {...input} onChange={onChange} bookEditions={props.bookEditions} />;

    case inputTypes.INDEXER_SELECT:
      return <IndexerSelectInputConnector {...presentation} {...input} onChange={onChange} value={optionalKey(value)} includeAny={optionalBoolean(props.includeAny)} isDisabled={isDisabled} />;

    case inputTypes.DOWNLOAD_CLIENT_SELECT:
      return <DownloadClientSelectInputConnector {...presentation} {...input} onChange={onChange} value={optionalKey(value)} includeAny={optionalBoolean(props.includeAny)} isDisabled={isDisabled} protocol={typeof props.protocol === 'string' ? props.protocol : undefined} />;

    case inputTypes.ROOT_FOLDER_SELECT:
      return <RootFolderSelectInputConnector
        {...presentation}
        {...input}
        onChange={onChange}
        value={optionalText(value)}
        isDisabled={isDisabled}
        includeMissingValue={optionalBoolean(props.includeMissingValue)}
        includeNoChange={optionalBoolean(props.includeNoChange)}
        includeNoChangeDisabled={optionalBoolean(props.includeNoChangeDisabled)}
        valueOptions={asStateRecord(props.valueOptions)}
        selectedValueOptions={asStateRecord(props.selectedValueOptions)}
      />;

    case inputTypes.DYNAMIC_SELECT:
      return <EnhancedSelectInputConnector
        {...presentation}
        {...input}
        onChange={onChange}
        {...providerProps}
        value={Array.isArray(value) ? providerKeys(value) : optionalKey(value)}
        isDisabled={isDisabled}
        selectOptionsProviderAction={typeof props.selectOptionsProviderAction === 'string' ? props.selectOptionsProviderAction : undefined}
      />;

    case inputTypes.TEXT_TAG:
      return <TextTagInputConnector {...presentation} {...input} onChange={onChange} isDisabled={isDisabled} />;

    case inputTypes.UMASK:
      return <UMaskInput {...presentation} {...input} onChange={onChange} value={optionalText(value)} isDisabled={isDisabled} />;

    default:
      return <TextInput
        {...presentation}
        value={typeof value === 'string' || typeof value === 'number' ? value : ''}
        onChange={onChange}
      />;
  }
}

function isMultipleSelectField(props: ScalarSelectField | MultipleSelectField): props is MultipleSelectField {
  return Array.isArray(props.value);
}

function renderControl(props: FormInputGroupProps, presentation: ControlPresentation, helpIds: { helpTextId?: string; helpTextWarningId?: string }) {
  switch (props.type) {
    case inputTypes.CHECK:
      return <CheckInput {...presentation} {...controlInputProps(props)} {...helpIds} />;

    case inputTypes.KEY_VALUE_LIST:
      return <KeyValueListInput {...presentation} {...controlInputProps(props)} />;

    case inputTypes.MONITOR_BOOKS_SELECT:
      return <MonitorBooksSelectInput {...presentation} {...controlInputProps(props)} />;

    case inputTypes.MONITOR_NEW_ITEMS_SELECT:
      return <MonitorNewItemsSelectInput {...presentation} {...controlInputProps(props)} />;

    case inputTypes.NUMBER:
      return <NumberInput {...presentation} {...controlInputProps(props)} />;

    case inputTypes.PASSWORD:
      return <PasswordInput {...presentation} {...controlInputProps(props)} />;

    case inputTypes.TEXT_AREA:
      return <TextArea {...presentation} {...controlInputProps(props)} />;

    case inputTypes.SELECT:
      if (isMultipleSelectField(props)) {
        return <EnhancedSelectInput {...presentation} {...controlInputProps(props)} value={props.value} onChange={props.onChange} />;
      }
      return <EnhancedSelectInput
        {...presentation}
        {...controlInputProps(props)}
        value={props.value}
        onChange={props.onChange}
        onEditableChange={props.onChange}
      />;

    case inputTypes.TAG:
      return <TagInputConnector {...presentation} {...controlInputProps(props)} />;

    case inputTypes.TAG_SELECT:
      return <TagSelectInputConnector {...presentation} {...controlInputProps(props)} />;

    case inputTypes.INDEXER_FLAGS_SELECT:
      return <IndexerFlagsSelectInput {...presentation} {...controlInputProps(props)} />;

    case inputTypes.TEXT:
    case inputTypes.FLOAT:
    case undefined:
      return <TextInput {...presentation} {...controlInputProps(props)} />;

    default:
      return renderProviderControl(props, presentation);
  }
}


function FormInputGroup(props: FormInputGroupProps) {
  const {
    className = styles.inputGroup,
    containerClassName = styles.inputGroupContainer,
    inputClassName,
    type = inputTypes.TEXT,
    id,
    name,
    unit,
    buttons = [],
    helpText,
    helpTexts = [],
    helpTextWarning,
    helpLink,
    errors = [],
    warnings = [],
    'aria-describedby': describedBy,
    'aria-invalid': invalid,
    'aria-label': ariaLabel,
    'aria-labelledby': labelledBy
  } = props;
  const association = useContext(FormGroupContext);
  const [generatedId] = useState(getUniqueElementId);
  const hasRowLabel = association !== null && association.name === name;
  const inputId = id ?? (hasRowLabel ? association.inputId : generatedId);
  const labelIds = labelledBy ?? (!ariaLabel && hasRowLabel ? association.labelId : undefined);

  const checkInput = type === inputTypes.CHECK;
  const hasError = !!errors.length;
  const hasWarning = !hasError && !!warnings.length;
  const buttonsArray = React.Children.toArray(buttons);
  const lastButtonIndex = buttonsArray.length - 1;
  const hasButton = !!buttonsArray.length;
  const helpId = helpText ? `${inputId}-help` : undefined;
  const helpWarningId = helpTextWarning ? `${inputId}-help-warning` : undefined;
  const descriptions = [
    ...(describedBy?.split(/\s+/) ?? []),
    // A standalone checkbox uses its inline text as its name. A checkbox
    // with a separate row label uses the same text as a description.
    (!checkInput || labelIds || ariaLabel) && helpId,
    (!checkInput || labelIds || ariaLabel || helpText) && helpWarningId,
    ...(checkInput ? [] : helpTexts.map((_, index) => `${inputId}-help-${index}`)),
    ...errors.map((_, index) => `${inputId}-error-${index}`),
    ...warnings.map((_, index) => `${inputId}-warning-${index}`)
  ];
  const checkedHelp = checkInput ? { helpTextId: helpId, helpTextWarningId: helpWarningId } : {};

  return (
    <div className={containerClassName}>
      <div className={className}>
        <div className={styles.inputContainer}>
          {renderControl(props, {
            className: inputClassName,
            id: inputId,
            name,
            'aria-describedby': Array.from(new Set(descriptions.filter(Boolean))).join(' ') || undefined,
            'aria-invalid': hasError ? true : invalid,
            'aria-label': ariaLabel,
            'aria-labelledby': labelIds,
            helpText,
            helpTextWarning,
            hasError,
            hasWarning,
            hasButton
          }, checkedHelp)}

          {
            unit &&
              <div
                className={
                  type === inputTypes.NUMBER ?
                    styles.inputUnitNumber :
                    styles.inputUnit
                }
              >
                {unit}
              </div>
          }
        </div>

        {
          buttonsArray.map((button, index) => {
            return React.isValidElement<FormInputButtonProps>(button) ?
              React.cloneElement(button, { isLastButton: index === lastButtonIndex }) :
              button;
          })
        }

      </div>

      {
        !checkInput && helpText &&
          <FormInputHelpText
            id={helpId}
            text={helpText}
          />
      }

      {
        !checkInput && helpTexts &&
          <div>
            {
              helpTexts.map((text, index) => {
                return (
                  <FormInputHelpText
                    key={index}
                    id={`${inputId}-help-${index}`}
                    text={text}
                    isCheckInput={checkInput}
                  />
                );
              })
            }
          </div>
      }

      {
        (!checkInput || helpText) && helpTextWarning &&
          <FormInputHelpText
            id={helpWarningId}
            text={helpTextWarning}
            isWarning={true}
          />
      }

      {
        helpLink &&
          <Link
            to={helpLink}
          >
            {translate('MoreInfo')}
          </Link>
      }

      {
        errors.map((error, index) => {
          return (
            <FormInputHelpText
              key={index}
              id={`${inputId}-error-${index}`}
              text={error.message}
              link={error.link}
              tooltip={error.detailedMessage}
              isError={true}
              isCheckInput={checkInput}
            />
          );
        })
      }

      {
        warnings.map((warning, index) => {
          return (
            <FormInputHelpText
              key={index}
              id={`${inputId}-warning-${index}`}
              text={warning.message}
              link={warning.link}
              tooltip={warning.detailedMessage}
              isWarning={true}
              isCheckInput={checkInput}
            />
          );
        })
      }
    </div>
  );
}

export default FormInputGroup;
