import { useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import type { ValueChange } from 'Components/Form/formTypes';
import { inputTypes } from 'Helpers/Props';
import type { SettingsProjection } from 'Store/Selectors/selectSettings';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import translate from 'Utilities/String/translate';
import { checkDraft } from '../../providerSettingsState';
import styles from './NotificationEventItems.css';

interface Props {
  item: SettingsProjection;
  onInputChange: (change: ValueChange<unknown>) => void;
}

function NotificationEventItems(props: Props) {
  const [id] = useState(getUniqueElementId);
  const labelId = `${id}-label`;
  const helpId = `${id}-help`;
  const {
    item,
    onInputChange
  } = props;

  const onGrab = checkDraft(item, 'onGrab');
  const onReleaseImport = checkDraft(item, 'onReleaseImport');
  const onUpgrade = checkDraft(item, 'onUpgrade');
  const onRename = checkDraft(item, 'onRename');
  const onAuthorAdded = checkDraft(item, 'onAuthorAdded');
  const onAuthorDelete = checkDraft(item, 'onAuthorDelete');
  const onBookDelete = checkDraft(item, 'onBookDelete');
  const onBookFileDelete = checkDraft(item, 'onBookFileDelete');
  const onBookFileDeleteForUpgrade = checkDraft(item, 'onBookFileDeleteForUpgrade');
  const onHealthIssue = checkDraft(item, 'onHealthIssue');
  const onHealthRestored = checkDraft(item, 'onHealthRestored');
  const onDownloadFailure = checkDraft(item, 'onDownloadFailure');
  const onImportFailure = checkDraft(item, 'onImportFailure');
  const onBookRetag = checkDraft(item, 'onBookRetag');
  const onApplicationUpdate = checkDraft(item, 'onApplicationUpdate');
  const supportsOnGrab = checkDraft(item, 'supportsOnGrab');
  const supportsOnReleaseImport = checkDraft(item, 'supportsOnReleaseImport');
  const supportsOnUpgrade = checkDraft(item, 'supportsOnUpgrade');
  const supportsOnRename = checkDraft(item, 'supportsOnRename');
  const supportsOnAuthorAdded = checkDraft(item, 'supportsOnAuthorAdded');
  const supportsOnAuthorDelete = checkDraft(item, 'supportsOnAuthorDelete');
  const supportsOnBookDelete = checkDraft(item, 'supportsOnBookDelete');
  const supportsOnBookFileDelete = checkDraft(item, 'supportsOnBookFileDelete');
  const supportsOnHealthIssue = checkDraft(item, 'supportsOnHealthIssue');
  const supportsOnHealthRestored = checkDraft(item, 'supportsOnHealthRestored');
  const includeHealthWarnings = checkDraft(item, 'includeHealthWarnings');
  const supportsOnDownloadFailure = checkDraft(item, 'supportsOnDownloadFailure');
  const supportsOnImportFailure = checkDraft(item, 'supportsOnImportFailure');
  const supportsOnBookRetag = checkDraft(item, 'supportsOnBookRetag');
  const supportsOnApplicationUpdate = checkDraft(item, 'supportsOnApplicationUpdate');

  return (
    <FormGroup role="group" aria-labelledby={labelId} aria-describedby={helpId}>
      <FormLabel id={labelId} component="span">
        {translate('NotificationTriggers')}
      </FormLabel>
      <div>
        <FormInputHelpText
          id={helpId}
          text="Select which events should trigger this notification"
          link="https://wiki.servarr.com/readarr/settings#connections"
        />
        <div className={styles.events}>
          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onGrab"
              helpText={translate('OnGrabHelpText')}
              isDisabled={!supportsOnGrab.value}
              {...onGrab}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onReleaseImport"
              helpText={translate('OnReleaseImportHelpText')}
              isDisabled={!supportsOnReleaseImport.value}
              {...onReleaseImport}
              onChange={onInputChange}
            />
          </div>

          {
            onReleaseImport.value &&
              <div>
                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="onUpgrade"
                  helpText={translate('OnUpgradeHelpText')}
                  isDisabled={!supportsOnUpgrade.value}
                  {...onUpgrade}
                  onChange={onInputChange}
                />
              </div>
          }

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onDownloadFailure"
              helpText={translate('OnDownloadFailureHelpText')}
              isDisabled={!supportsOnDownloadFailure.value}
              {...onDownloadFailure}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onImportFailure"
              helpText={translate('OnImportFailureHelpText')}
              isDisabled={!supportsOnImportFailure.value}
              {...onImportFailure}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onRename"
              helpText={translate('OnRenameHelpText')}
              isDisabled={!supportsOnRename.value}
              {...onRename}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onAuthorAdded"
              helpText={translate('OnAuthorAddedHelpText')}
              isDisabled={!supportsOnAuthorAdded.value}
              {...onAuthorAdded}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onAuthorDelete"
              helpText={translate('OnAuthorDeleteHelpText')}
              isDisabled={!supportsOnAuthorDelete.value}
              {...onAuthorDelete}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onBookDelete"
              helpText={translate('OnBookDeleteHelpText')}
              isDisabled={!supportsOnBookDelete.value}
              {...onBookDelete}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onBookFileDelete"
              helpText={translate('OnBookFileDeleteHelpText')}
              isDisabled={!supportsOnBookFileDelete.value}
              {...onBookFileDelete}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onBookFileDeleteForUpgrade"
              helpText={translate('OnBookFileDeleteForUpgradeHelpText')}
              isDisabled={!supportsOnBookFileDelete.value}
              {...onBookFileDeleteForUpgrade}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onBookRetag"
              helpText={translate('OnBookRetagHelpText')}
              isDisabled={!supportsOnBookRetag.value}
              {...onBookRetag}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onApplicationUpdate"
              helpText={translate('OnApplicationUpdateHelpText')}
              isDisabled={!supportsOnApplicationUpdate.value}
              {...onApplicationUpdate}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onHealthIssue"
              helpText={translate('OnHealthIssueHelpText')}
              isDisabled={!supportsOnHealthIssue.value}
              {...onHealthIssue}
              onChange={onInputChange}
            />
          </div>

          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="onHealthRestored"
              helpText={translate('OnHealthRestoredHelpText')}
              isDisabled={!supportsOnHealthRestored.value}
              {...onHealthRestored}
              onChange={onInputChange}
            />
          </div>

          {
            (onHealthIssue.value || onHealthRestored.value) &&
              <div>
                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="includeHealthWarnings"
                  helpText={translate('IncludeHealthWarningsHelpText')}
                  isDisabled={!supportsOnHealthIssue.value && !supportsOnHealthRestored.value}
                  {...includeHealthWarnings}
                  onChange={onInputChange}
                />
              </div>
          }

        </div>
      </div>
    </FormGroup>
  );
}

export default NotificationEventItems;
