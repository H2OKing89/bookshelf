import { Component } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
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
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import translate from 'Utilities/String/translate';
import { checkDraft, providerEditDrafts, type ProviderEditProps } from '../../providerSettingsState';
import styles from './EditDownloadClientModalContent.css';

interface Props extends ProviderEditProps {
  onDeleteDownloadClientPress?: () => void;

}

class EditDownloadClientModalContent extends Component<Props> {

  //
  // Render

  render() {
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
      onAdvancedSettingsPress,
      onDeleteDownloadClientPress,
      ...otherProps
    } = this.props;

    const { id, implementationName, name, enable, protocol, priority, fields, tags, message } = providerEditDrafts(item);
    const removeCompletedDownloads = checkDraft(item, 'removeCompletedDownloads');
    const removeFailedDownloads = checkDraft(item, 'removeFailedDownloads');

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {`${id ? 'Edit' : 'Add'} Download Client - ${implementationName}`}
        </ModalHeader>

        <ModalBody>
          {
            isFetching &&
              <LoadingIndicator />
          }

          {
            !isFetching && !!error &&
              <div>
                {translate('UnableToAddANewDownloadClientPleaseTryAgain')}
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
                    {translate('Enable')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="enable"
                    {...enable}
                    onChange={onInputChange}
                  />
                </FormGroup>

                {
                  fields.map((field) => {
                    return (
                      <ProviderFieldFormGroup
                        key={field.name}
                        advancedSettings={advancedSettings}
                        provider="downloadClient"
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
                    {translate('ClientPriority')}
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

                <FormGroup>
                  <FormLabel>{translate('Tags')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.TAG}
                    name="tags"
                    helpText={translate('DownloadClientTagHelpText')}
                    {...tags}
                    onChange={onInputChange}
                  />
                </FormGroup>

                <FieldSet
                  size={sizes.SMALL}
                  legend={translate('CompletedDownloadHandling')}
                >
                  <FormGroup>
                    <FormLabel>{translate('RemoveCompleted')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.CHECK}
                      name="removeCompletedDownloads"
                      helpText={translate('RemoveCompletedDownloadsHelpText')}
                      {...removeCompletedDownloads}
                      onChange={onInputChange}
                    />
                  </FormGroup>

                  {
                    protocol.value !== 'torrent' &&
                      <FormGroup>
                        <FormLabel>{translate('RemoveFailed')}</FormLabel>

                        <FormInputGroup
                          type={inputTypes.CHECK}
                          name="removeFailedDownloads"
                          helpText={translate('RemoveFailedDownloadsHelpText')}
                          {...removeFailedDownloads}
                          onChange={onInputChange}
                        />
                      </FormGroup>
                  }
                </FieldSet>
              </Form>
          }
        </ModalBody>
        <ModalFooter>
          {
            !!id &&
              <Button
                className={styles.deleteButton}
                kind={kinds.DANGER}
                onPress={onDeleteDownloadClientPress}
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
}

export default EditDownloadClientModalContent;
