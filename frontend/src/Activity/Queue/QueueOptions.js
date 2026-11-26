import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { setQueueIncludeUnknownAuthorItems, useIncludeUnknownAuthorItems } from './queuePreferences';

export default function QueueOptions() {
  const includeUnknownAuthorItems = useIncludeUnknownAuthorItems();

  return (
    <FormGroup>
      <FormLabel>
        {translate('ShowUnknownAuthorItems')}
      </FormLabel>

      <FormInputGroup
        type={inputTypes.CHECK}
        name="includeUnknownAuthorItems"
        value={includeUnknownAuthorItems}
        helpText={translate('IncludeUnknownAuthorItemsHelpText')}
        onChange={({ value }) => setQueueIncludeUnknownAuthorItems(value)}
      />
    </FormGroup>
  );
}
