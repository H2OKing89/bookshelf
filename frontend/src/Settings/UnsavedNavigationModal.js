import React, { useLayoutEffect, useState } from 'react';
import { useHistory } from 'react-router-dom';
import PendingChangesModal from './PendingChangesModal';

export default function UnsavedNavigationModal() {
  const { unsavedChanges } = useHistory();
  const [isOpen, setIsOpen] = useState(false);

  useLayoutEffect(() => unsavedChanges.listen(setIsOpen), [unsavedChanges]);

  return (
    <PendingChangesModal
      isOpen={isOpen}
      onConfirm={() => unsavedChanges.resolve(true)}
      onCancel={() => unsavedChanges.resolve(false)}
    />
  );
}
