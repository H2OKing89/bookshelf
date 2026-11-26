import { createAction } from 'redux-actions';
import { isStateRecord, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { set } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import { requestLegacy } from './Creators/legacyRequest';
import { createSectionRequests } from './Creators/sectionRequests';

export const section = 'paths';
const requests = createSectionRequests();

export const defaultState = {
  currentPath: '',
  isPopulated: false,
  isFetching: false,
  error: null,
  directories: [],
  files: [],
  parent: null
};

interface FilesystemEntry extends StateRecord {
  path: string;
  name: string | null;
}

interface FilesystemResult extends StateRecord {
  path: string;
  directories: FilesystemEntry[];
  files: FilesystemEntry[];
  parent: string | null;
}

function entries(value: unknown): value is FilesystemEntry[] {
  return Array.isArray(value) && value.every((item: unknown) =>
    isStateRecord(item) && typeof item.path === 'string' && (item.name == null || typeof item.name === 'string'));
}

export function parsePaths(path: string, data: unknown): FilesystemResult {
  if (!isStateRecord(data) || !entries(data.directories) || !entries(data.files) ||
      (data.parent != null && typeof data.parent !== 'string')) {
    throw new ApiError({ kind: 'invalid-response' });
  }

  return { path, directories: data.directories, files: data.files, parent: typeof data.parent === 'string' ? data.parent : null };
}

export const FETCH_PATHS = 'paths/fetchPaths';
export const UPDATE_PATHS = 'paths/updatePaths';
export const CLEAR_PATHS = 'paths/clearPaths';
const RESET_PATHS = 'paths/resetPaths';

export const fetchPaths = createThunk(FETCH_PATHS);
export const updatePaths = createAction<FilesystemResult>(UPDATE_PATHS);
export const clearPaths = createThunk(CLEAR_PATHS);
const resetPaths = createAction<void>(RESET_PATHS);

export const actionHandlers = handleThunks({
  [FETCH_PATHS]: function(getState, payload, dispatch) {
    const { path: requestedPath, allowFoldersWithoutTrailingSlashes = false, includeFiles = false } = payload;
    const path = requestedPath ?? '';
    if (typeof path !== 'string' || typeof allowFoldersWithoutTrailingSlashes !== 'boolean' || typeof includeFiles !== 'boolean') {
      requests.cancel(getState, section);
      dispatch(set({ section, isFetching: false, isPopulated: false, error: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }

    const attempt = requests.begin(getState, section);
    dispatch(set({ section, isFetching: true }));
    const { request, abortRequest } = requestLegacy({
      url: '/filesystem',
      data: { path, allowFoldersWithoutTrailingSlashes, includeFiles }
    });
    attempt.attachAbort(abortRequest);

    request.done((data) => {
      if (!attempt.finish()) {
        return;
      }
      try {
        dispatch(updatePaths(parsePaths(path, data)));
        dispatch(set({ section, isFetching: false, isPopulated: true, error: null }));
      } catch (error) {
        dispatch(set({ section, isFetching: false, isPopulated: false, error }));
      }
    });

    request.fail((xhr) => {
      if (attempt.finish()) {
        dispatch(set({ section, isFetching: false, isPopulated: false, error: isStateRecord(xhr) && xhr.aborted === true ? null : xhr }));
      }
    });
  },
  [CLEAR_PATHS]: function(getState, _payload, dispatch) {
    requests.cancel(getState, section);
    dispatch(resetPaths());
  }
});

export const reducers = createHandleActions({
  [UPDATE_PATHS]: (state, { payload }) => {
    if (!isStateRecord(payload) || typeof payload.path !== 'string') {
      return state;
    }
    try {
      const parsed = parsePaths(payload.path, payload);
      return { ...state, currentPath: parsed.path, directories: parsed.directories, files: parsed.files, parent: parsed.parent };
    } catch {
      return state;
    }
  },
  [RESET_PATHS]: (state) => ({ ...state, ...defaultState })
}, defaultState, section);
