import { connect, type ConnectedProps } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import EditCustomFormatModal, { type EditFormatModalProps } from './EditCustomFormatModal';

const connector = connect(null, { clearPendingChanges });
type Props = EditFormatModalProps & ConnectedProps<typeof connector>;

function EditCustomFormatModalConnector({ clearPendingChanges, onModalClose, ...props }: Props) {
  return <EditCustomFormatModal {...props} onModalClose={() => {
    clearPendingChanges({ section: 'settings.customFormats' });
    clearPendingChanges({ section: 'settings.customFormatSpecifications' });
    onModalClose();
  }} />;
}

export default connector(EditCustomFormatModalConnector);
