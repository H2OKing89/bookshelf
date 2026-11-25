import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useBlocker } from 'react-router-dom';
import PendingChangesModal from './PendingChangesModal';
import { type DiscardChanges, UnsavedNavigationContext } from './useUnsavedNavigation';

function warnBeforeUnload(event: BeforeUnloadEvent) {
  event.preventDefault();
  event.returnValue = true;
}

interface UnloadListenerTarget {
  addEventListener(type: 'beforeunload', listener: (event: BeforeUnloadEvent) => void): void;
  removeEventListener(type: 'beforeunload', listener: (event: BeforeUnloadEvent) => void): void;
}

export function createUnsavedChanges(browser: UnloadListenerTarget, onChange: (isDirty: boolean) => void) {
  const forms = new Set<{ onDiscard?: DiscardChanges }>();

  return {
    hasChanges: () => forms.size > 0,
    register(onDiscard?: DiscardChanges) {
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
function UnsavedNavigationProvider({ children }: { children: React.ReactNode }) {
  const [isDirty, setIsDirty] = useState(false);
  const [changes] = useState(() => createUnsavedChanges(window, setIsDirty));
  const blocker = useBlocker(changes.hasChanges);
  const resolved = useRef<typeof blocker | null>(null);

  const resolve = useCallback((proceed: boolean) => {
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

export default UnsavedNavigationProvider;
