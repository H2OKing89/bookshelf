import { connect, type ConnectedProps } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import EditSpecificationModal, { type EditSpecificationModalProps } from './EditSpecificationModal';

const connector = connect(null, { clearPendingChanges });
type Props = EditSpecificationModalProps & ConnectedProps<typeof connector>;

function EditSpecificationModalConnector({ clearPendingChanges, onModalClose, ...props }: Props) {
  return <EditSpecificationModal {...props} onModalClose={() => {
    clearPendingChanges({ section: 'settings.customFormatSpecifications' });
    onModalClose();
  }} />;
}

export default connector(EditSpecificationModalConnector);
