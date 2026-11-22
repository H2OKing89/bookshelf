import { createBrowserHistory, createPath } from 'history';

const unsavedChangesPrompt = 'bookshelf:unsaved-changes';
const entryPositionKey = 'bookshelfNavigationPosition';

function readEntryPosition() {
  const position = window.history.state?.[entryPositionKey];

  if (typeof position?.stack === 'string' && Number.isSafeInteger(position.index)) {
    return position;
  }

  return null;
}

function saveEntryPosition(position = { stack: Math.random().toString(36).slice(2), index: 0 }) {
  try {
    window.history.replaceState({ ...window.history.state, [entryPositionKey]: position }, '');
    return position;
  } catch {
    // Standard history still handles entries from the current page session.
    return null;
  }
}

function warnBeforeUnload(event) {
  event.preventDefault();
  event.returnValue = true;
}

export default function createAppHistory() {
  const listeners = new Set();
  const dirtyForms = new Set();
  let currentPosition = readEntryPosition() ?? saveEntryPosition();
  let unblock = null;
  let pendingTransition = null;
  let restoreDelta = null;
  let isRestoring = false;

  function publish(nextTransition) {
    pendingTransition = nextTransition;
    listeners.forEach((listener) => listener(nextTransition !== null));
  }

  const history = createBrowserHistory({
    getUserConfirmation(message, callback) {
      if (message === unsavedChangesPrompt) {
        // Let history finish its original transition. Replaying it would lose
        // the direction of a browser POP or the semantics of a REPLACE.
        publish({ commit: callback, restoreDelta });
      } else {
        // eslint-disable-next-line no-alert
        return callback(window.confirm(message));
      }
    }
  });

  function resolve(confirmed) {
    const transition = pendingTransition;

    if (!transition) {
      return;
    }

    publish(null);

    if (confirmed) {
      [...dirtyForms].forEach((form) => form.onDiscard?.());
      transition.commit(true);
    } else if (transition.restoreDelta) {
      // history v4 only remembers traversal positions since startup. Our
      // native entry metadata also supports cancellation after a reload.
      isRestoring = true;
      history.go(transition.restoreDelta);
    } else {
      transition.commit(false);
    }
  }

  history.listen((location, action) => {
    isRestoring = false;

    if (action === 'POP') {
      currentPosition = readEntryPosition() ?? saveEntryPosition();
    } else {
      const offset = action === 'PUSH' ? 1 : 0;
      currentPosition = saveEntryPosition(currentPosition ? { ...currentPosition, index: currentPosition.index + offset } : undefined);
    }
  });

  function requestTransition(location, action) {
    // The browser has already moved on a POP. A newer POP supersedes the old
    // callback, and a POP back to our current entry needs no confirmation.
    if (action === 'POP') {
      if (location.key === history.location.key && createPath(location) === createPath(history.location)) {
        publish(null);
        return true;
      }

      const destination = readEntryPosition();
      restoreDelta = destination && currentPosition?.stack === destination.stack ?
        currentPosition.index - destination.index :
        null;

      return unsavedChangesPrompt;
    }

    restoreDelta = null;

    return pendingTransition || isRestoring ? false : unsavedChangesPrompt;
  }

  history.unsavedChanges = {
    register(onDiscard) {
      const form = { onDiscard };
      dirtyForms.add(form);

      if (dirtyForms.size === 1) {
        unblock = history.block(requestTransition);
        window.addEventListener('beforeunload', warnBeforeUnload);
      }

      return () => {
        if (dirtyForms.delete(form) && dirtyForms.size === 0) {
          unblock();
          window.removeEventListener('beforeunload', warnBeforeUnload);
          resolve(false);
        }
      };
    },

    listen(listener) {
      listeners.add(listener);
      listener(pendingTransition !== null);

      return () => listeners.delete(listener);
    },

    resolve
  };

  return history;
}
