import { Component } from 'react';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import translate from 'Utilities/String/translate';
import type { AuthorHistoryViewProps } from './authorHistoryState';
import AuthorHistoryTableContent from './AuthorHistoryTableContent';

class AuthorHistoryModalContent extends Component<AuthorHistoryViewProps & { onModalClose: () => void }> {

  //
  // Render

  render() {
    const {
      onModalClose
    } = this.props;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('History')}
        </ModalHeader>

        <ModalBody>
          <AuthorHistoryTableContent
            {...this.props}
          />
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>
            {translate('Close')}
          </Button>
        </ModalFooter>
      </ModalContent>
    );
  }
}

export default AuthorHistoryModalContent;
