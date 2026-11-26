import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import translate from 'Utilities/String/translate';
import { checkDraft, numberDraft, providerEditDrafts, type ProviderEditProps,textDraft } from '../../providerSettingsState';
import styles from './EditIndexerModalContent.css';

const rejectionActions = [
  { key: 'review', value: 'Warn and review' },
  { key: 'blocklist', value: 'Mark failed and blocklist' },
  { key: 'blocklistAndRetry', value: 'Mark failed, blocklist and search again' }
];

const rejectionFields = [
  { name: 'dangerousFileAction', label: 'Potentially dangerous files' },
  { name: 'executableFileAction', label: 'Executable files' },
  { name: 'userRejectedExtensionAction', label: 'User-rejected file extensions' }
];

interface Props extends ProviderEditProps {
  onDeleteIndexerPress?: () => void;

}

function EditIndexerModalContent(props: Props) {
  const {
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
    onDeleteIndexerPress,
    onAdvancedSettingsPress,
    ...otherProps
  } = props;

  const { id, implementationName, name, tags, fields, priority, protocol } = providerEditDrafts(item);
  const enableRss = checkDraft(item, 'enableRss');
  const enableAutomaticSearch = checkDraft(item, 'enableAutomaticSearch');
  const enableInteractiveSearch = checkDraft(item, 'enableInteractiveSearch');
  const supportsRss = checkDraft(item, 'supportsRss');
  const supportsSearch = checkDraft(item, 'supportsSearch');
  const downloadClientId = numberDraft(item, 'downloadClientId');

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {`${id ? 'Edit' : 'Add'} Indexer - ${implementationName}`}
      </ModalHeader>

      <ModalBody>
        {
          isFetching &&
            <LoadingIndicator />
        }

        {
          !isFetching && !!error &&
            <div>
              {translate('UnableToAddANewIndexerPleaseTryAgain')}
            </div>
        }

        {
          !isFetching && !error &&
            <Form {...otherProps}>
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
                  {translate('EnableRSS')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableRss"
                  helpText={supportsRss.value ? translate('EnableRssHelpText') : undefined}
                  helpTextWarning={supportsRss.value ? undefined : translate('SupportsRssvalueRSSIsNotSupportedWithThisIndexer')}
                  isDisabled={!supportsRss.value}
                  {...enableRss}
                  onChange={onInputChange}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  {translate('EnableAutomaticSearch')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableAutomaticSearch"
                  helpText={supportsSearch.value ? translate('SupportsSearchvalueWillBeUsedWhenAutomaticSearchesArePerformedViaTheUIOrByReadarr') : undefined}
                  helpTextWarning={supportsSearch.value ? undefined : translate('SupportsSearchvalueSearchIsNotSupportedWithThisIndexer')}
                  isDisabled={!supportsSearch.value}
                  {...enableAutomaticSearch}
                  onChange={onInputChange}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>
                  {translate('EnableInteractiveSearch')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableInteractiveSearch"
                  helpText={supportsSearch.value ? translate('SupportsSearchvalueWillBeUsedWhenInteractiveSearchIsUsed') : undefined}
                  helpTextWarning={supportsSearch.value ? undefined : translate('SupportsSearchvalueSearchIsNotSupportedWithThisIndexer')}
                  isDisabled={!supportsSearch.value}
                  {...enableInteractiveSearch}
                  onChange={onInputChange}
                />
              </FormGroup>

              {
                fields.map((field) => {
                  return (
                    <ProviderFieldFormGroup
                      key={field.name}
                      advancedSettings={advancedSettings}
                      provider="indexer"
                      providerData={item}
                      {...field}
                      onChange={onFieldChange}
                    />
                  );
                })
              }

              <FormGroup
                advancedSettings={advancedSettings}
                isAdvanced={true}
              >
                <FormLabel>
                  {translate('IndexerPriority')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="priority"
                  helpText={translate('IndexerPriorityHelpText')}
                  min={1}
                  max={50}
                  {...priority}
                  onChange={onInputChange}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={advancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('DownloadClient')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.DOWNLOAD_CLIENT_SELECT}
                  name="downloadClientId"
                  helpText={translate('IndexerDownloadClientHelpText')}
                  {...downloadClientId}
                  includeAny={true}
                  protocol={protocol.value}
                  onChange={onInputChange}
                />
              </FormGroup>

              {
                rejectionFields.map(({ name: fieldName, label }) => (
                  <FormGroup
                    key={fieldName}
                    advancedSettings={advancedSettings}
                    isAdvanced={true}
                  >
                    <FormLabel>{label}</FormLabel>
                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name={fieldName}
                      values={rejectionActions}
                      helpText="Applies only to completed downloads from this indexer with an unambiguous rejected-file inventory and no imported parts. Keeps the download and files in the client; search again also requires Failed Download Handling to allow automatic redownload."
                      {...textDraft(item, fieldName)}
                      onChange={onInputChange}
                    />
                  </FormGroup>
                ))
              }

              <FormGroup>
                <FormLabel>
                  {translate('Tags')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.TAG}
                  name="tags"
                  helpText={translate('IndexerTagsHelpText')}
                  {...tags}
                  onChange={onInputChange}
                />
              </FormGroup>
            </Form>
        }
      </ModalBody>
      <ModalFooter>
        {
          !!id &&
            <Button
              className={styles.deleteButton}
              kind={kinds.DANGER}
              onPress={onDeleteIndexerPress}
            >
              Delete
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
          Test
        </SpinnerErrorButton>

        <Button
          onPress={onModalClose}
        >
          Cancel
        </Button>

        <SpinnerErrorButton
          action="save"
          isSpinning={isSaving}
          error={saveError}
          onPress={onSavePress}
        >
          Save
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditIndexerModalContent;
