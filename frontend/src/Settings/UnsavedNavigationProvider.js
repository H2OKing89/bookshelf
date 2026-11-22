import PropTypes from 'prop-types';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useBlocker } from 'react-router-dom';
import PendingChangesModal from './PendingChangesModal';
import { UnsavedNavigationContext } from './useUnsavedNavigation';

function warnBeforeUnload(event) {
  event.preventDefault();
  event.returnValue = true;
}

export function createUnsavedChanges(browser, onChange) {
  const forms = new Set();

  return {
    hasChanges: () => forms.size > 0,
    register(onDiscard) {
      const form = { onDiscard };
      forms.add(form);

      if (forms.size === 1) {
        browser.addEventListener('beforeunload', warnBeforeUnload);
        onChange(true);
      }

      return () => {
        if (forms.delete(form) && forms.size === 0) {
          browser.removeEventListener('beforeunload', warnBeforeUnload);
          onChange(false);
        }
      };
    },
    discard() {
      [...forms].forEach((form) => form.onDiscard?.());
    }
  };
}

// React Router permits one blocker per router. The page and any open editors
// register independently so closing a child cannot remove the parent's guard.
function UnsavedNavigationProvider({ children }) {
  const [isDirty, setIsDirty] = useState(false);
  const [changes] = useState(() => createUnsavedChanges(window, setIsDirty));
  const blocker = useBlocker(changes.hasChanges);
  const resolved = useRef(null);

  const resolve = useCallback((proceed) => {
    if (blocker.state !== 'blocked' || blocker === resolved.current) {
      return;
    }

    resolved.current = blocker;
    if (proceed) {
      changes.discard();
      blocker.proceed();
    } else {
      blocker.reset();
    }
  }, [blocker, changes]);

  useEffect(() => {
    if (!isDirty) {
      resolve(false);
    }
  }, [isDirty, resolve]);

  return (
    <UnsavedNavigationContext.Provider value={changes.register}>
      {children}
      <PendingChangesModal
        isOpen={isDirty && blocker.state === 'blocked'}
        onConfirm={() => resolve(true)}
        onCancel={() => resolve(false)}
      />
    </UnsavedNavigationContext.Provider>
  );
}

UnsavedNavigationProvider.propTypes = {
  children: PropTypes.node.isRequired
};

export default UnsavedNavigationProvider;
