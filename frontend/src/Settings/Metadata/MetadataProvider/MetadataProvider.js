import PropTypes from 'prop-types';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

const writeAudioTagOptions = [
  {
    key: 'no',
    get value() {
      return translate('WriteTagsNo');
    }
  },
  {
    key: 'sync',
    get value() {
      return translate('WriteTagsSync');
    }
  },
  {
    key: 'allFiles',
    get value() {
      return translate('WriteTagsAll');
    }
  },
  {
    key: 'newFiles',
    get value() {
      return translate('WriteTagsNew');
    }
  }
];

const audnexusRegionOptions = [
  { key: 'au', value: 'Australia' },
  { key: 'ca', value: 'Canada' },
  { key: 'de', value: 'Germany' },
  { key: 'es', value: 'Spain' },
  { key: 'fr', value: 'France' },
  { key: 'in', value: 'India' },
  { key: 'it', value: 'Italy' },
  { key: 'jp', value: 'Japan' },
  { key: 'us', value: 'United States' },
  { key: 'uk', value: 'United Kingdom' }
];

function MetadataProvider(props) {
  const {
    isFetching,
    error,
    settings,
    hasSettings,
    onInputChange
  } = props;

  return (

    <div>
      {
        isFetching &&
          <LoadingIndicator />
      }

      {
        !isFetching && error &&
          <Alert kind={kinds.DANGER}>
            {translate('UnableToLoadMetadataProviderSettings')}
          </Alert>
      }

      {
        hasSettings && !isFetching && !error &&
          <Form>
            <FieldSet legend={translate('AudiobookCatalog')}>
              <FormGroup>
                <FormLabel>{translate('AudnexusUrl')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="audnexusUrl"
                  helpText={translate('AudnexusUrlHelpText')}
                  onChange={onInputChange}
                  {...settings.audnexusUrl}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('AudnexusRegion')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="audnexusRegion"
                  values={audnexusRegionOptions}
                  helpText={translate('AudnexusRegionHelpText')}
                  onChange={onInputChange}
                  {...settings.audnexusRegion}
                />
              </FormGroup>
            </FieldSet>

            <FieldSet legend="audible-rs">
              <FormGroup>
                <FormLabel>{translate('AudibleRsEnabled')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="audibleRsEnabled"
                  helpText={translate('AudibleRsEnabledHelpText')}
                  onChange={onInputChange}
                  {...settings.audibleRsEnabled}
                />
              </FormGroup>

              {
                settings.audibleRsEnabled.value &&
                  <div>
                    <FormGroup>
                      <FormLabel>{translate('AudibleRsUrl')}</FormLabel>

                      <FormInputGroup
                        type={inputTypes.TEXT}
                        name="audibleRsUrl"
                        onChange={onInputChange}
                        {...settings.audibleRsUrl}
                      />
                    </FormGroup>

                    <FormGroup>
                      <FormLabel>{translate('AudibleRsTokenFile')}</FormLabel>

                      <FormInputGroup
                        type={inputTypes.TEXT}
                        name="audibleRsTokenFile"
                        helpText={translate('AudibleRsTokenFileHelpText')}
                        onChange={onInputChange}
                        {...settings.audibleRsTokenFile}
                      />
                    </FormGroup>

                    <FormGroup>
                      <FormLabel>{translate('AudibleRsAccount')}</FormLabel>

                      <FormInputGroup
                        type={inputTypes.TEXT}
                        name="audibleRsAccount"
                        helpText={translate('AudibleRsAccountHelpText')}
                        onChange={onInputChange}
                        {...settings.audibleRsAccount}
                      />
                    </FormGroup>
                  </div>
              }
            </FieldSet>

            <FieldSet legend={translate('AudioFileMetadata')}>
              <FormGroup>
                <FormLabel>{translate('PreferPathAsin')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="preferPathAsin"
                  helpText={translate('PreferPathAsinHelpText')}
                  onChange={onInputChange}
                  {...settings.preferPathAsin}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('WriteAudioTags')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="writeAudioTags"
                  helpTextWarning={translate('WriteBookTagsHelpTextWarning')}
                  helpLink="https://wiki.servarr.com/readarr/settings#write-metadata-to-audio-files"
                  values={writeAudioTagOptions}
                  onChange={onInputChange}
                  {...settings.writeAudioTags}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('WriteAudioTagsScrub')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="scrubAudioTags"
                  helpTextWarning={translate('WriteAudioTagsScrubHelp')}
                  onChange={onInputChange}
                  {...settings.scrubAudioTags}
                />
              </FormGroup>

            </FieldSet>
          </Form>
      }
    </div>

  );
}

MetadataProvider.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  settings: PropTypes.object.isRequired,
  hasSettings: PropTypes.bool.isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default MetadataProvider;
