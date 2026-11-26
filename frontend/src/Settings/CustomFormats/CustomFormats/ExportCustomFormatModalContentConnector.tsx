import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import selectSettings from 'Store/Selectors/selectSettings';
import { asStateRecord, getItemId, isStateRecord, type LegacyRootState } from 'Store/StateTypes';
import { exportCustomFormat } from '../customFormatState';
import ExportCustomFormatModalContent from './ExportCustomFormatModalContent';

interface OwnProps { id: number; onModalClose: () => void }

function createMapStateToProps() {
  return createSelector((state: LegacyRootState) => state.settings.customFormats, (_state: LegacyRootState, props: OwnProps) => props.id, (value, id) => {
    const section = asStateRecord(value);
    const items: unknown[] = Array.isArray(section.items) ? section.items : [];
    const item = items.find((candidate) => getItemId(candidate) === id);
    // Export the saved record, never the shared edit draft or another format's
    // conditions. Merely opening Export must not reset the condition editor.
    const saved = isStateRecord(item) ? item : null;
    return {
      isFetching: section.isFetching === true,
      error: section.error,
      json: saved ? exportCustomFormat(selectSettings(saved, {}, null).settings) : '',
      specificationsPopulated: saved !== null && Array.isArray(saved.specifications)
    };
  });
}

export default connect(createMapStateToProps)(ExportCustomFormatModalContent);
