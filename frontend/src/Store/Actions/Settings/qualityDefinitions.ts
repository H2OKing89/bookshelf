import _ from 'lodash';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { set, update } from 'Store/Actions/baseActions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import { requestLegacy } from 'Store/Actions/Creators/legacyRequest';
import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';
import { createThunk, type ThunkHandler } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';
import { parseSettingsList, type SettingsResource } from './settingsResponses';

const section = 'settings.qualityDefinitions';

export const FETCH_QUALITY_DEFINITIONS = 'settings/qualityDefinitions/fetchQualityDefinitions';
export const SAVE_QUALITY_DEFINITIONS = 'settings/qualityDefinitions/saveQualityDefinitions';
export const SET_QUALITY_DEFINITION_VALUE = 'settings/qualityDefinitions/setQualityDefinitionValue';

export const fetchQualityDefinitions = createThunk(FETCH_QUALITY_DEFINITIONS);
export const saveQualityDefinitions = createThunk(SAVE_QUALITY_DEFINITIONS);

interface QualityDefinitionValue {
  id: number;
  name: 'title' | 'minSize' | 'maxSize';
  value: string | number | null;
}

export const setQualityDefinitionValue = createAction<QualityDefinitionValue>(SET_QUALITY_DEFINITION_VALUE);

function isEditableValue(name: unknown, value: unknown): boolean {
  return name === 'title' ? typeof value === 'string' :
    (name === 'minSize' || name === 'maxSize') && (value === null || typeof value === 'number' && Number.isFinite(value) && value >= 0);
}

function parseDefinitions(value: unknown): SettingsResource[] {
  const definitions = parseSettingsList(value);
  if (!definitions.every((item) => isStateRecord(item.quality) && typeof item.title === 'string' &&
    typeof item.weight === 'number' && Number.isFinite(item.weight) &&
    isEditableValue('minSize', item.minSize ?? null) && isEditableValue('maxSize', item.maxSize ?? null)
  )) {
    throw new TypeError('Invalid quality definitions');
  }
  return definitions;
}

const saveDefinitions: ThunkHandler = (getState, _payload, dispatch) => {
  const settings = asStateRecord(getSectionState(getState(), section, true));
  if (settings.isSaving === true) {
    return;
  }
  const submitted = _.cloneDeep(asStateRecord(settings.pendingChanges));
  if (!Object.keys(submitted).length) {
    return;
  }
  let changed: SettingsResource[] = [];
  try {
    const items = parseDefinitions(settings.items);
    changed = Object.entries(submitted).map(([key, value]) => {
      const item = items.find((entry) => String(entry.id) === key);
      if (!item || !isStateRecord(value) || !Object.entries(value).every(([name, field]) => isEditableValue(name, field))) {
        throw new TypeError('Invalid quality draft');
      }
      return { ...item, ...value };
    });
  } catch {
    dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-request' }) }));
    return;
  }

  dispatch(set({ section, isSaving: true }));
  const { request } = requestLegacy({
    method: 'PUT',
    url: '/qualityDefinition/update',
    data: JSON.stringify(changed),
    contentType: 'application/json',
    dataType: 'json'
  });

  request.done((data) => {
    let items: SettingsResource[] = [];
    try {
      items = parseDefinitions(data);
      const previous = parseDefinitions(settings.items);
      if (items.length !== previous.length || !previous.every((item) => items.some((returned) => returned.id === item.id))) {
        throw new TypeError('Incomplete quality response');
      }
    } catch {
      dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-response' }) }));
      return;
    }

    const pending = asStateRecord(asStateRecord(getSectionState(getState(), section, true)).pendingChanges);
    const remaining: StateRecord = {};
    for (const [id, value] of Object.entries(pending)) {
      const submittedRow = asStateRecord(submitted[id]);
      const fields = Object.fromEntries(Object.entries(asStateRecord(value)).filter(([name, field]) =>
        !Object.hasOwn(submittedRow, name) || !_.isEqual(submittedRow[name], field)
      ));
      if (Object.keys(fields).length) {
        remaining[id] = fields;
      }
    }
    dispatch(batchActions([
      update({ section, data: items }),
      set({ section, isSaving: false, saveError: null, pendingChanges: remaining })
    ]));
  });

  request.fail((xhr) => {
    dispatch(set({ section, isSaving: false, saveError: xhr }));
  });
};

function updateQualityDraft(state: StateRecord, { payload }: { payload: unknown }): StateRecord {
  if (!isStateRecord(payload) || typeof payload.id !== 'number' ||
      !Number.isSafeInteger(payload.id) || payload.id <= 0 || typeof payload.name !== 'string' ||
      !isEditableValue(payload.name, payload.value)) {
    return state;
  }
  const { id, name, value } = payload;
  const current = getSectionState(state, section);
  const items: unknown[] = Array.isArray(current.items) ? current.items : [];
  const item = items.find((entry) => isStateRecord(entry) && entry.id === id);
  if (!isStateRecord(item)) {
    return state;
  }
  const pendingChanges = { ...asStateRecord(current.pendingChanges) };
  const pending = { ...asStateRecord(pendingChanges[id]) };
  if (item[name] === value || item[name] == null && value === null) {
    delete pending[name];
  } else {
    pending[name] = value;
  }

  if (Object.keys(pending).length) {
    pendingChanges[id] = pending;
  } else {
    delete pendingChanges[id];
  }
  return updateSectionState(state, section, { ...current, pendingChanges });
}

export default {
  defaultState: { isFetching: false, isPopulated: false, error: null, items: [], isSaving: false, saveError: null, pendingChanges: {} },
  actionHandlers: {
    [FETCH_QUALITY_DEFINITIONS]: createFetchHandler(section, '/qualitydefinition', parseDefinitions),
    [SAVE_QUALITY_DEFINITIONS]: saveDefinitions
  },
  reducers: { [SET_QUALITY_DEFINITION_VALUE]: updateQualityDraft }
};
