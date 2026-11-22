const positionKey = 'bookshelfNavigationPosition';

export default function migrateBrowserHistory(browser) {
  const previous = browser.history.state?.[positionKey];
  if (typeof previous?.stack !== 'string' || !Number.isSafeInteger(previous.index)) {
    return null;
  }

  function updateEntry() {
    const state = browser.history.state;
    const saved = state?.[positionKey];

    if (saved?.stack === previous.stack && Number.isSafeInteger(saved.index) && state.idx == null) {
      browser.history.replaceState({ ...state, idx: saved.index, usr: state.state ?? null }, '');
    } else if (!saved && Number.isSafeInteger(state?.idx)) {
      // Keep the originating position on new entries so a reload can still
      // index the remaining history entries made by the previous build.
      browser.history.replaceState({ ...state, [positionKey]: { stack: previous.stack, index: state.idx } }, '');
    }
  }

  updateEntry();
  browser.addEventListener('popstate', updateEntry, true);

  return updateEntry;
}
