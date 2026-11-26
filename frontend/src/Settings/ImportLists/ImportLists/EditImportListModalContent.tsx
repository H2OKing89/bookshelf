import React from 'react';
import AuthorMonitorNewItemsOptionsPopoverContent from 'AddAuthor/AuthorMonitorNewItemsOptionsPopoverContent';
import Alert from 'Components/Alert';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Popover from 'Components/Tooltip/Popover';
import { icons, inputTypes, kinds, tooltipPositions } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import { asStateRecord } from 'Store/StateTypes';
import formatShortTimeSpan from 'Utilities/Date/formatShortTimeSpan';
import requestAction from 'Utilities/requestAction';
import translate from 'Utilities/String/translate';
import { checkDraft, numberDraft, providerEditDrafts, type ProviderEditProps,textDraft } from '../../providerSettingsState';
import ImportListMembership from './ImportListMembership';
import styles from './EditImportListModalContent.css';

function ImportListMonitoringOptionsPopoverContent() {
  return (
    <DescriptionList>
      <DescriptionListItem
        title={translate('None')}
        data={translate('DataListMonitorNone')}
      />

      <DescriptionListItem
        title={translate('SpecificBook')}
        data={translate('DataListMonitorSpecificBook')}
      />

      <DescriptionListItem
        title={translate('AllAuthorBooks')}
        data={translate('DataListMonitorAll')}
      />
    </DescriptionList>
  );
}

interface Props extends ProviderEditProps {
  onDeleteImportListPress?: () => void;
  id?: number;
  showMetadataProfile: boolean;
}

function EditImportListModalContent(props: Props) {

  const monitorOptions = [
    { key: 'none', value: 'None' },
    { key: 'specificBook', value: 'Specific Book' },
    { key: 'entireAuthor', value: 'All Author Books' }
  ];

  const {
    id,
    advancedSettings,
    isFetching,
    error,
    isSaving,
    isTesting,
    saveError,
    item,
    onInputChange,
    onFieldChange,
    onModalClose,
    onSavePress,
    onTestPress,
    onAdvancedSettingsPress,
    onDeleteImportListPress,
    showMetadataProfile,
    ...otherProps
  } = props;

  const { name, tags, fields, message } = providerEditDrafts(item);
  const enableAutomaticAdd = checkDraft(item, 'enableAutomaticAdd');
  const shouldMonitorExisting = checkDraft(item, 'shouldMonitorExisting');
  const shouldSearch = checkDraft(item, 'shouldSearch');
  const minRefreshInterval = textDraft(item, 'minRefreshInterval');
  const shouldMonitor = textDraft(item, 'shouldMonitor');
  const rootFolderPath = textDraft(item, 'rootFolderPath');
  const monitorNewItems = textDraft(item, 'monitorNewItems');
  const tagExisting = textDraft(item, 'tagExisting');
  const qualityProfileId = numberDraft(item, 'qualityProfileId');
  const metadataProfileId = numberDraft(item, 'metadataProfileId');

  const [authState, setAuthState] = React.useState<{ isAuthenticating: boolean; authError: string | null; listOptions: unknown; authedOnce: boolean }>({
    isAuthenticating: false,
    authError: null,
    listOptions: null,
    authedOnce: false
  });

  const isHardcover = textDraft(item, 'implementation').value === 'HardcoverImport';

  const onAuthenticatePress = () => {
    if (authState.isAuthenticating) {
      return;
    }

    setAuthState((prev) => ({
      ...prev,
      isAuthenticating: true,
      authError: null
    }));

    requestAction({
      provider: 'importlist',
      action: 'auth',
      providerData: item
    }).then((response: unknown) => {
      const { success, options, error: responseError } = asStateRecord(response);

      if (success === true) {
        setAuthState({
          isAuthenticating: false,
          authError: null,
          listOptions: options,
          authedOnce: true
        });
      } else {
        setAuthState({
          isAuthenticating: false,
          authError: typeof responseError === 'string' && responseError ? responseError : translate('UnableToAuthenticate'),
          listOptions: null,
          authedOnce: false
        });
      }
    }).catch(() => {
      setAuthState({
        isAuthenticating: false,
        authError: translate('UnableToAuthenticate'),
        listOptions: null,
        authedOnce: false
      });
    });
  };

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {id ? translate('EditList') : translate('AddList')}
      </ModalHeader>

      <ModalBody>
        {
          isFetching &&
            <LoadingIndicator />
        }

        {
          !isFetching && !!error &&
            <div>
              {translate('UnableToAddANewListPleaseTryAgain')}
            </div>
        }

        {
          !isFetching && !error &&
            <Form {...otherProps}>
              {
                !!message &&
                  <Alert
                    className={styles.message}
                    kind={message.type}
                  >
                    {message.message}
                  </Alert>
              }

              <Alert
                kind={kinds.INFO}
                className={styles.message}
              >
                {translate('ListWillRefreshEveryInterp', { 0: formatShortTimeSpan(minRefreshInterval.value) })}
              </Alert>

              <FieldSet legend={translate('ImportListSettings')} >
                <FormGroup>
                  <FormLabel>
                    {translate('Name')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="name"
                    {...name}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel>
                    {translate('EnableAutomaticAdd')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="enableAutomaticAdd"
                    helpText={translate('EnableAutomaticAddHelpText')}
                    {...enableAutomaticAdd}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel>
                    {translate('Monitor')}

                    <Popover
                      anchor={
                        <Icon
                          className={styles.labelIcon}
                          name={icons.INFO}
                        />
                      }
                      title={translate('MonitoringOptions')}
                      body={<ImportListMonitoringOptionsPopoverContent />}
                      position={tooltipPositions.RIGHT}
                    />
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="shouldMonitor"
                    values={monitorOptions}
                    helpText={translate('ShouldMonitorHelpText')}
                    {...shouldMonitor}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel>
                    {translate('ShouldMonitorExisting')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="shouldMonitorExisting"
                    helpText={translate('ShouldMonitorExistingHelpText')}
                    {...shouldMonitorExisting}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel>
                    {translate('SearchForNewItems')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="shouldSearch"
                    helpText={translate('ShouldSearchHelpText')}
                    {...shouldSearch}
                    onChange={onInputChange}
                  />
                </FormGroup>
              </FieldSet>

              <FieldSet legend={translate('AddedAuthorSettings')} >
                <FormGroup>
                  <FormLabel>
                    {translate('RootFolder')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.ROOT_FOLDER_SELECT}
                    name="rootFolderPath"
                    helpText={translate('RootFolderPathHelpText')}
                    {...rootFolderPath}
                    value={rootFolderPath?.value ?? ''}
                    includeMissingValue={true}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel>
                    {translate('MonitorNewItems')}
                    <Popover
                      anchor={
                        <Icon
                          className={styles.labelIcon}
                          name={icons.INFO}
                        />
                      }
                      title={translate('MonitorNewItems')}
                      body={<AuthorMonitorNewItemsOptionsPopoverContent />}
                      position={tooltipPositions.RIGHT}
                    />
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.MONITOR_NEW_ITEMS_SELECT}
                    name="monitorNewItems"
                    helpText={translate('MonitorNewItemsHelpText')}
                    {...monitorNewItems}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel>
                    {translate('QualityProfile')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.QUALITY_PROFILE_SELECT}
                    name="qualityProfileId"
                    helpText={translate('QualityProfileIdHelpText')}
                    {...qualityProfileId}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup className={showMetadataProfile ? undefined : styles.hideMetadataProfile}>
                  <FormLabel>
                    {translate('MetadataProfile')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.METADATA_PROFILE_SELECT}
                    name="metadataProfileId"
                    helpText={translate('MetadataProfileIdHelpText')}
                    {...metadataProfileId}
                    includeNone={true}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel>
                    {translate('ReadarrTags')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.TAG}
                    name="tags"
                    helpText={translate('TagsHelpText')}
                    {...tags}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel>Tag existing authors</FormLabel>

                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="tagExisting"
                    values={[
                      { key: 'none', value: 'None' },
                      { key: 'add', value: 'Add' },
                      { key: 'sync', value: 'Synchronize' }
                    ]}
                    helpText="Add keeps tags permanently; Synchronize removes only tags this list system supplied after complete successful membership refreshes. Manually assigned tags are preserved. Tags apply to authors."
                    {...tagExisting}
                    onChange={onInputChange}
                  />
                </FormGroup>
              </FieldSet>

              {
                !!fields && !!fields.length &&
                  <FieldSet legend={translate('ImportListSpecificSettings')} >
                    {
                      isHardcover &&
                        <div className={styles.hardcoverAuthRow}>
                          <FormGroup>
                            <FormLabel>{translate('APIKey')}</FormLabel>
                            <div className={styles.hardcoverAuthButtons}>
                              <SpinnerErrorButton
                                isSpinning={authState.isAuthenticating}
                                error={authState.authError ? { message: authState.authError } : null}
                                kind={kinds.DEFAULT}
                                onPress={onAuthenticatePress}
                              >
                                Authenticate
                              </SpinnerErrorButton>
                              {
                                authState.authedOnce && !authState.authError &&
                                  <span className={styles.authSuccess}>Authenticated</span>
                              }
                            </div>
                          </FormGroup>
                        </div>
                    }
                    {
                      fields.map((field) => {
                        const isListField = isHardcover && field.name === 'listIds';

                        return (
                          <ProviderFieldFormGroup
                            key={field.name}
                            advancedSettings={advancedSettings}
                            provider="importList"
                            providerData={item}
                            section="settings.importLists"
                            {...field}
                            selectOptions={isListField && authState.listOptions ? authState.listOptions : field.selectOptions}
                            pending={isListField ? authState.isAuthenticating || field.pending : field.pending}
                            onChange={onFieldChange}
                          />
                        );
                      })
                    }
                  </FieldSet>
              }

            </Form>
        }
        {!!id && <ImportListMembership id={id} />}
      </ModalBody>
      <ModalFooter>
        {
          !!id &&
            <Button
              className={styles.deleteButton}
              kind={kinds.DANGER}
              onPress={onDeleteImportListPress}
            >
              {translate('Delete')}
            </Button>
        }

        <AdvancedSettingsButton
          advancedSettings={advancedSettings}
          onAdvancedSettingsPress={onAdvancedSettingsPress}
          showLabel={false}
        />

        <SpinnerErrorButton
          action="test"
          isSpinning={isTesting}
          error={saveError}
          onPress={onTestPress}
        >
          {translate('Test')}
        </SpinnerErrorButton>

        <Button
          onPress={onModalClose}
        >
          {translate('Cancel')}
        </Button>

        <SpinnerErrorButton
          action="save"
          isSpinning={isSaving}
          error={saveError}
          onPress={onSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditImportListModalContent;
