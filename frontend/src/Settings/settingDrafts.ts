import type { ValueChange } from 'Components/Form/formTypes';
import type createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import type { SettingDraft, SettingsProjection } from 'Store/Selectors/selectSettings';
import { isStateRecord } from 'Store/StateTypes';
import { checkValue, numberValue, textValue } from '../Components/Form/formValues';

export interface SettingsFieldsProps {
  settings: SettingsProjection;
  onInputChange: (change: ValueChange<unknown>) => void;
}

export type SettingsViewProps = ReturnType<ReturnType<typeof createSettingsSectionSelector>> & SettingsFieldsProps & {
  onSavePress: () => void;
};

export function getDraft(settings: SettingsProjection, key: string): SettingDraft | undefined {
  const draft = settings[key];
  return draft && typeof draft === 'object' && !Array.isArray(draft) ? draft : undefined;
}

// These projections only narrow values for presentation; the store retains
// the original pending/saved values and validation messages for submission.
export function readDraft<T>(settings: SettingsProjection, key: string, decode: (value: unknown) => T) {
  const draft = getDraft(settings, key);

  return {
    ...draft,
    value: decode(draft?.value),
    errors: draft?.errors ?? [],
    warnings: draft?.warnings ?? []
  };
}

interface Account {
  identifier: string;
  username: string;
  hasPassword: boolean;
}

function accountValues(value: unknown): Account[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.filter((account): account is Account =>
    isStateRecord(account) && typeof account.identifier === 'string' &&
    typeof account.username === 'string' && typeof account.hasPassword === 'boolean'
  );
}

export function generalDrafts(settings: SettingsProjection) {
  return {
    bindAddress: readDraft(settings, 'bindAddress', textValue),
    trustedNetworks: readDraft(settings, 'trustedNetworks', textValue),
    allowedHosts: readDraft(settings, 'allowedHosts', textValue),
    urlBase: readDraft(settings, 'urlBase', textValue),
    instanceName: readDraft(settings, 'instanceName', textValue),
    applicationUrl: readDraft(settings, 'applicationUrl', textValue),
    sslCertPath: readDraft(settings, 'sslCertPath', textValue),
    sslKeyPath: readDraft(settings, 'sslKeyPath', textValue),
    sslCertPassword: readDraft(settings, 'sslCertPassword', textValue),
    authenticationMethod: readDraft(settings, 'authenticationMethod', textValue),
    authenticationRequired: readDraft(settings, 'authenticationRequired', textValue),
    username: readDraft(settings, 'username', textValue),
    password: readDraft(settings, 'password', textValue),
    passwordConfirmation: readDraft(settings, 'passwordConfirmation', textValue),
    apiKey: readDraft(settings, 'apiKey', textValue),
    certificateValidation: readDraft(settings, 'certificateValidation', textValue),
    userIdentifier: readDraft(settings, 'userIdentifier', textValue),
    proxyType: readDraft(settings, 'proxyType', textValue),
    proxyHostname: readDraft(settings, 'proxyHostname', textValue),
    proxyUsername: readDraft(settings, 'proxyUsername', textValue),
    proxyPassword: readDraft(settings, 'proxyPassword', textValue),
    proxyBypassFilter: readDraft(settings, 'proxyBypassFilter', textValue),
    logLevel: readDraft(settings, 'logLevel', textValue),
    branch: readDraft(settings, 'branch', textValue),
    updateMechanism: readDraft(settings, 'updateMechanism', textValue),
    updateScriptPath: readDraft(settings, 'updateScriptPath', textValue),
    backupFolder: readDraft(settings, 'backupFolder', textValue),
    port: readDraft(settings, 'port', numberValue),
    sslPort: readDraft(settings, 'sslPort', numberValue),
    proxyPort: readDraft(settings, 'proxyPort', numberValue),
    backupInterval: readDraft(settings, 'backupInterval', numberValue),
    backupRetention: readDraft(settings, 'backupRetention', numberValue),
    enableSsl: readDraft(settings, 'enableSsl', checkValue),
    performanceProfilingEnabled: readDraft(settings, 'performanceProfilingEnabled', checkValue),
    launchBrowser: readDraft(settings, 'launchBrowser', checkValue),
    proxyEnabled: readDraft(settings, 'proxyEnabled', checkValue),
    proxyBypassLocalAddresses: readDraft(settings, 'proxyBypassLocalAddresses', checkValue),
    updateAutomatically: readDraft(settings, 'updateAutomatically', checkValue),
    accounts: readDraft(settings, 'accounts', accountValues)
  };
}

export function uiDrafts(settings: SettingsProjection) {
  return {
    calendarWeekColumnHeader: readDraft(settings, 'calendarWeekColumnHeader', textValue),
    shortDateFormat: readDraft(settings, 'shortDateFormat', textValue),
    longDateFormat: readDraft(settings, 'longDateFormat', textValue),
    timeFormat: readDraft(settings, 'timeFormat', textValue),
    theme: readDraft(settings, 'theme', textValue),
    showRelativeDates: readDraft(settings, 'showRelativeDates', checkValue),
    enableColorImpairedMode: readDraft(settings, 'enableColorImpairedMode', checkValue),
    firstDayOfWeek: readDraft(settings, 'firstDayOfWeek', numberValue),
    uiLanguage: readDraft(settings, 'uiLanguage', numberValue)
  };
}

export function mediaManagementDrafts(settings: SettingsProjection) {
  return {
    userRejectedExtensions: readDraft(settings, 'userRejectedExtensions', textValue),
    extraFileExtensions: readDraft(settings, 'extraFileExtensions', textValue),
    downloadPropersAndRepacks: readDraft(settings, 'downloadPropersAndRepacks', textValue),
    rescanAfterRefresh: readDraft(settings, 'rescanAfterRefresh', textValue),
    allowFingerprinting: readDraft(settings, 'allowFingerprinting', textValue),
    fileDate: readDraft(settings, 'fileDate', textValue),
    recycleBin: readDraft(settings, 'recycleBin', textValue),
    chmodFolder: readDraft(settings, 'chmodFolder', textValue),
    chownGroup: readDraft(settings, 'chownGroup', textValue),
    createEmptyAuthorFolders: readDraft(settings, 'createEmptyAuthorFolders', checkValue),
    deleteEmptyFolders: readDraft(settings, 'deleteEmptyFolders', checkValue),
    skipFreeSpaceCheckWhenImporting: readDraft(settings, 'skipFreeSpaceCheckWhenImporting', checkValue),
    checkFreeSpaceWhenGrabbing: readDraft(settings, 'checkFreeSpaceWhenGrabbing', checkValue),
    copyUsingHardlinks: readDraft(settings, 'copyUsingHardlinks', checkValue),
    importExtraFiles: readDraft(settings, 'importExtraFiles', checkValue),
    autoUnmonitorPreviouslyDownloadedBooks: readDraft(settings, 'autoUnmonitorPreviouslyDownloadedBooks', checkValue),
    watchLibraryForChanges: readDraft(settings, 'watchLibraryForChanges', checkValue),
    setPermissionsLinux: readDraft(settings, 'setPermissionsLinux', checkValue),
    minimumFreeSpaceWhenImporting: readDraft(settings, 'minimumFreeSpaceWhenImporting', numberValue),
    recycleBinCleanupDays: readDraft(settings, 'recycleBinCleanupDays', numberValue)
  };
}
