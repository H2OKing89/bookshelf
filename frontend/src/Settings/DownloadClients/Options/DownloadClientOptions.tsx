import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import {
  type DownloadClientOption,
  type DownloadClientOptionsDraft,
  type DownloadClientOptionsResource,
  downloadClientOptionValidation
} from './downloadClientOptionsState';

interface DownloadClientOptionsProps {
  advancedSettings: boolean;
  isFetching: boolean;
  isSaving: boolean;
  loadError: boolean;
  saveError: Error | null;
  saved: DownloadClientOptionsResource | undefined;
  draft: DownloadClientOptionsDraft;
  onInputChange: (event: { name: string; value: unknown }) => void;
}

function DownloadClientOptions({
  advancedSettings,
  isFetching,
  isSaving,
  loadError,
  saveError,
  saved,
  draft,
  onInputChange
}: DownloadClientOptionsProps) {
  function field(name: DownloadClientOption) {
    return {
      value: draft[name] ?? saved?.[name],
      pending: draft[name] !== undefined,
      isDisabled: isSaving,
      ...downloadClientOptionValidation(saveError, name)
    };
  }

  return (
    <div>
      {
        isFetching &&
          <LoadingIndicator />
      }

      {
        loadError &&
          <Alert kind={kinds.DANGER}>
            {translate('UnableToLoadDownloadClientOptions')}
          </Alert>
      }

      {
        saveError &&
          <div role="alert">
            <Alert kind={kinds.DANGER}>{translate('SaveFailed')}</Alert>
          </div>
      }

      {
        saved && advancedSettings &&
          <div>
            <FieldSet legend={translate('CompletedDownloadHandling')}>
              <Form>
                <FormGroup
                  advancedSettings={advancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('Enable')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="enableCompletedDownloadHandling"
                    helpText={translate('EnableCompletedDownloadHandlingHelpText')}
                    onChange={onInputChange}
                    {...field('enableCompletedDownloadHandling')}
                  />
                </FormGroup>
              </Form>
            </FieldSet>

            <FieldSet
              legend={translate('FailedDownloadHandling')}
            >
              <Form>
                <FormGroup
                  advancedSettings={advancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('AutoRedownloadFailed')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="autoRedownloadFailed"
                    helpText={translate('AutoRedownloadFailedHelpText')}
                    onChange={onInputChange}
                    {...field('autoRedownloadFailed')}
                  />
                </FormGroup>

                {
                  (draft.autoRedownloadFailed ?? saved.autoRedownloadFailed) ?
                    <FormGroup
                      advancedSettings={advancedSettings}
                      isAdvanced={true}
                      size={sizes.MEDIUM}
                    >
                      <FormLabel>{translate('AutoRedownloadFailedFromInteractiveSearch')}</FormLabel>

                      <FormInputGroup
                        type={inputTypes.CHECK}
                        name="autoRedownloadFailedFromInteractiveSearch"
                        helpText={translate('AutoRedownloadFailedFromInteractiveSearchHelpText')}
                        onChange={onInputChange}
                        {...field('autoRedownloadFailedFromInteractiveSearch')}
                      />
                    </FormGroup> :
                    null
                }
              </Form>

              <Alert kind={kinds.INFO}>
                {translate('RemoveDownloadsAlert')}
              </Alert>
            </FieldSet>
          </div>
      }
    </div>
  );
}

export default DownloadClientOptions;
