import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import translate from 'Utilities/String/translate';
import RootFolderSelectInput, { type RootFolderOption, type RootFolderSelectInputProps } from './RootFolderSelectInput';

const ADD_NEW_KEY = 'addNew';
type OwnProps = Omit<RootFolderSelectInputProps, 'values' | 'isSaving' | 'saveError'> & {
  includeMissingValue?: boolean;
  includeNoChangeDisabled?: boolean;
};

export function getRootFolderOptions(
  source: unknown,
  value: string | null | undefined,
  includeMissingValue = false,
  includeNoChange = false,
  includeNoChangeDisabled = true
): RootFolderOption[] {
  const items = asStateRecord(source).items;
  const values: RootFolderOption[] = Array.isArray(items) ? items.flatMap((item: unknown) => {
    const folder = asStateRecord(item);
    if (typeof folder.path !== 'string') {
      return [];
    }

    return [{
      key: folder.path,
      value: folder.path,
      name: typeof folder.name === 'string' ? folder.name : undefined,
      freeSpace: typeof folder.freeSpace === 'number' ? folder.freeSpace : null,
      isMissing: false
    }];
  }) : [];

  if (includeNoChange) {
    values.unshift({
      key: 'noChange',
      value: translate('NoChange'),
      isDisabled: includeNoChangeDisabled,
      isMissing: false
    });
  }

  if (!values.length) {
    values.push({ key: '', value: '', isDisabled: true, isHidden: true });
  }

  if (includeMissingValue && typeof value === 'string' && !values.some((option) => option.key === value)) {
    values.push({ key: value, value, isMissing: true, isDisabled: true });
  }

  values.push({ key: ADD_NEW_KEY, value: 'Add a new path' });

  return values;
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.rootFolders,
    (_state: LegacyRootState, props: OwnProps) => props.value,
    (_state: LegacyRootState, props: OwnProps) => props.includeMissingValue,
    (_state: LegacyRootState, props: OwnProps) => props.includeNoChange,
    (_state: LegacyRootState, props: OwnProps) => props.includeNoChangeDisabled,
    (rootFolders, value, includeMissingValue, includeNoChange, includeNoChangeDisabled) => {
      const section = asStateRecord(rootFolders);

      return {
        values: getRootFolderOptions(section, value, includeMissingValue, includeNoChange, includeNoChangeDisabled),
        isSaving: section.isSaving === true,
        saveError: section.saveError
      };
    }
  );
}

const connector = connect(createMapStateToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class RootFolderSelectInputConnector extends Component<Props> {
  componentDidMount() {
    const { name, value, values, onChange } = this.props;

    if (!value || !values.some((option) => option.key === value) || value === ADD_NEW_KEY) {
      const defaultValue = values[0]?.key;
      onChange({ name, value: defaultValue === ADD_NEW_KEY ? '' : defaultValue ?? '' });
    }
  }

  componentDidUpdate(prevProps: Props) {
    const { name, value, values, onChange } = this.props;
    if (prevProps.values === values) {
      return;
    }

    if (!value && values.some((option) => !!option.key && option.key !== ADD_NEW_KEY)) {
      const defaultValue = values[0]?.key;
      if (defaultValue !== undefined && defaultValue !== ADD_NEW_KEY) {
        onChange({ name, value: defaultValue });
      }
    }
  }

  render() {
    return <RootFolderSelectInput {...this.props} />;
  }
}

export default connector(RootFolderSelectInputConnector);
