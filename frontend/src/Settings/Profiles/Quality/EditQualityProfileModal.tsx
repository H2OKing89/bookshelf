import { Component } from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditQualityProfileModalContentConnector, { type QualityProfileEditorProps } from './EditQualityProfileModalContentConnector';

export type QualityProfileModalProps = Omit<QualityProfileEditorProps, 'onContentHeightChange'> & { isOpen: boolean };

class EditQualityProfileModal extends Component<QualityProfileModalProps, { height: 'auto' | number }> {

  //
  // Lifecycle

  constructor(props: QualityProfileModalProps) {
    super(props);

    this.state = {
      height: 'auto'
    };
  }

  //
  // Listeners

  onContentHeightChange = (height: number) => {
    if (this.state.height === 'auto' || height > this.state.height) {
      this.setState({ height });
    }
  };

  //
  // Render

  render() {
    const {
      isOpen,
      onModalClose,
      ...otherProps
    } = this.props;

    return (
      <Modal
        style={{ height: this.state.height === 'auto' ? 'auto' : `${this.state.height}px` }}
        isOpen={isOpen}
        size={sizes.EXTRA_LARGE}
        onModalClose={onModalClose}
      >
        <EditQualityProfileModalContentConnector
          {...otherProps}
          id={this.props.id}
          onContentHeightChange={this.onContentHeightChange}
          onModalClose={onModalClose}
        />
      </Modal>
    );
  }
}

export default EditQualityProfileModal;
