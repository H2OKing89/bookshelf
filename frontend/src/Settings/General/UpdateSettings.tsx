import { useSelector } from 'react-redux';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import createSystemStatusSelector from 'Store/Selectors/createSystemStatusSelector';
import { asStateRecord } from 'Store/StateTypes';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import { generalDrafts, type SettingsFieldsProps } from '../settingDrafts';

interface UpdateSettingsProps extends SettingsFieldsProps {
  advancedSettings: boolean;
  isWindows: boolean;
  packageUpdateMechanism: string;
}

const branchValues = [
  'master',
  'develop',
  'nightly'
];

function UpdateSettings(props: UpdateSettingsProps) {
  const { builtInUpdatesAvailable, isDocker } = asStateRecord(useSelector(createSystemStatusSelector()));
  const {
    advancedSettings,
    settings,
    packageUpdateMechanism,
    onInputChange
  } = props;

  const {
    branch,
    updateAutomatically,
    updateMechanism,
    updateScriptPath
  } = generalDrafts(settings);

  if (!advancedSettings) {
    return null;
  }

  if (builtInUpdatesAvailable === false) {
    return (
      <FieldSet legend={translate('Updates')}>
        <Alert kind={kinds.INFO}>
          {translate(isDocker ? 'BookshelfContainerUpdates' : 'BookshelfUpdatesUnavailable')}
        </Alert>
      </FieldSet>
    );
  }

  const usingExternalUpdateMechanism = packageUpdateMechanism !== 'builtIn';

  const updateOptions = [];

  if (usingExternalUpdateMechanism) {
    updateOptions.push({
      key: packageUpdateMechanism,
      value: titleCase(packageUpdateMechanism)
    });
  } else {
    updateOptions.push({ key: 'builtIn', value: translate('BuiltIn') });
  }

  updateOptions.push({ key: 'script', value: translate('Script') });

  return (
    <FieldSet legend={translate('Updates')}>
      <FormGroup
        advancedSettings={advancedSettings}
        isAdvanced={true}
      >
        <FormLabel>{translate('Branch')}</FormLabel>

        <FormInputGroup
          type={inputTypes.AUTO_COMPLETE}
          name="branch"
          helpText={usingExternalUpdateMechanism ? translate('BranchUpdateMechanism') : translate('BranchUpdate')}
          helpLink="https://wiki.servarr.com/readarr/settings#updates"
          {...branch}
          values={branchValues}
          onChange={onInputChange}
          readOnly={usingExternalUpdateMechanism}
        />
      </FormGroup>

      <div>
        <FormGroup
          advancedSettings={advancedSettings}
          isAdvanced={true}
          size={sizes.MEDIUM}
        >
          <FormLabel>{translate('Automatic')}</FormLabel>

          <FormInputGroup
            type={inputTypes.CHECK}
            name="updateAutomatically"
            helpText={translate('UpdateAutomaticallyHelpText')}
            helpTextWarning={updateMechanism.value === 'docker' ? translate('AutomaticUpdatesDisabledDocker') : undefined}
            onChange={onInputChange}
            {...updateAutomatically}
          />
        </FormGroup>

        <FormGroup
          advancedSettings={advancedSettings}
          isAdvanced={true}
        >
          <FormLabel>{translate('Mechanism')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="updateMechanism"
            values={updateOptions}
            helpText={translate('UpdateMechanismHelpText')}
            helpLink="https://wiki.servarr.com/readarr/settings#updates"
            onChange={onInputChange}
            {...updateMechanism}
          />
        </FormGroup>

        {
          updateMechanism.value === 'script' &&
            <FormGroup
              advancedSettings={advancedSettings}
              isAdvanced={true}
            >
              <FormLabel>{translate('ScriptPath')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT}
                name="updateScriptPath"
                helpText={translate('UpdateScriptPathHelpText')}
                onChange={onInputChange}
                {...updateScriptPath}
              />
            </FormGroup>
        }
      </div>
    </FieldSet>
  );
}

export default UpdateSettings;
