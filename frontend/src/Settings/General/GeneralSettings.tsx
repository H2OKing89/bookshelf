import _ from 'lodash';
import { Component } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import SettingsToolbarConnector from 'Settings/SettingsToolbarConnector';
import translate from 'Utilities/String/translate';
import { getDraft, type SettingsViewProps } from '../settingDrafts';
import BackupSettings from './BackupSettings';
import HostSettings from './HostSettings';
import LoggingSettings from './LoggingSettings';
import ProxySettings from './ProxySettings';
import SecuritySettings from './SecuritySettings';
import UpdateSettings from './UpdateSettings';

interface GeneralSettingsProps extends SettingsViewProps {
  advancedSettings: boolean;
  isResettingApiKey: boolean;
  isWindows: boolean;
  isWindowsService: boolean;
  mode: string;
  packageUpdateMechanism: string;
  onConfirmResetApiKey: () => void;
  onConfirmRestart: () => void;
}

const requiresRestartKeys = [
  'bindAddress',
  'port',
  'trustedNetworks',
  'allowedHosts',
  'urlBase',
  'enableSsl',
  'sslPort',
  'sslCertPath',
  'sslKeyPath',
  'sslCertPassword',
  'performanceProfilingEnabled'
];

class GeneralSettings extends Component<GeneralSettingsProps, { isRestartRequiredModalOpen: boolean }> {

  //
  // Lifecycle

  constructor(props: GeneralSettingsProps) {
    super(props);

    this.state = {
      isRestartRequiredModalOpen: false
    };
  }

  componentDidUpdate(prevProps: GeneralSettingsProps) {
    const {
      settings,
      isSaving,
      saveError,
      isResettingApiKey
    } = this.props;

    if (!isResettingApiKey && prevProps.isResettingApiKey) {
      this.setState({ isRestartRequiredModalOpen: true });
      return;
    }

    if (isSaving || saveError || !prevProps.isSaving) {
      return;
    }

    const prevSettings = prevProps.settings;

    const pendingRestart = _.some(requiresRestartKeys, (key) => {
      const setting = getDraft(settings, key);
      const prevSetting = getDraft(prevSettings, key);

      if (!setting || !prevSetting) {
        return false;
      }

      const previousValue = prevSetting.previousValue;
      const value = setting.value;

      // Saved SSL passwords are always masked. Replacing a masked password
      // with a new nonempty one still requires a restart even when the
      // response contains the same placeholder as before.
      if (key === 'sslCertPassword' && prevSetting.pending && previousValue != null && previousValue !== prevSetting.value) {
        return true;
      }

      return previousValue != null && previousValue !== value;
    });

    this.setState({ isRestartRequiredModalOpen: pendingRestart });
  }

  //
  // Listeners

  onConfirmRestart = () => {
    this.setState({ isRestartRequiredModalOpen: false });
    this.props.onConfirmRestart();
  };

  onCloseRestartRequiredModalOpen = () => {
    this.setState({ isRestartRequiredModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      advancedSettings,
      isFetching,
      isPopulated,
      error,
      settings,
      hasSettings,
      isResettingApiKey,
      isWindows,
      isWindowsService,
      mode,
      packageUpdateMechanism,
      onInputChange,
      onConfirmResetApiKey,
      ...otherProps
    } = this.props;

    return (
      <PageContent title={translate('GeneralSettings')}>
        <SettingsToolbarConnector
          {...otherProps}
        />

        <PageContentBody>
          {
            isFetching && !isPopulated &&
              <LoadingIndicator />
          }

          {
            !isFetching && !!error &&
              <Alert kind={kinds.DANGER}>
                {translate('UnableToLoadGeneralSettings')}
              </Alert>
          }

          {
            hasSettings && isPopulated && !error &&
              <Form
                {...otherProps}
              >
                <HostSettings
                  advancedSettings={advancedSettings}
                  settings={settings}
                  isWindows={isWindows}
                  mode={mode}
                  onInputChange={onInputChange}
                />

                <SecuritySettings
                  settings={settings}
                  isResettingApiKey={isResettingApiKey}
                  onInputChange={onInputChange}
                  onConfirmResetApiKey={onConfirmResetApiKey}
                />

                <ProxySettings
                  settings={settings}
                  onInputChange={onInputChange}
                />

                <LoggingSettings
                  settings={settings}
                  onInputChange={onInputChange}
                />

                <UpdateSettings
                  advancedSettings={advancedSettings}
                  settings={settings}
                  isWindows={isWindows}
                  packageUpdateMechanism={packageUpdateMechanism}
                  onInputChange={onInputChange}
                />

                <BackupSettings
                  advancedSettings={advancedSettings}
                  settings={settings}
                  onInputChange={onInputChange}
                />
              </Form>
          }
        </PageContentBody>

        <ConfirmModal
          isOpen={this.state.isRestartRequiredModalOpen}
          kind={kinds.DANGER}
          title={translate('RestartReadarr')}
          message={
            `Readarr requires a restart to apply changes, do you want to restart now? ${isWindowsService ? 'Depending which user is running the Readarr service you may need to restart Readarr as admin once before the service will start automatically.' : ''}`
          }
          cancelLabel={translate('IllRestartLater')}
          confirmLabel={translate('RestartNow')}
          onConfirm={this.onConfirmRestart}
          onCancel={this.onCloseRestartRequiredModalOpen}
        />
      </PageContent>
    );
  }

}

export default GeneralSettings;
