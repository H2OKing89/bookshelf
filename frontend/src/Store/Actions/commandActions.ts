import { batchActions } from 'redux-batched-actions';
import { messageTypes } from 'Helpers/Props';
import { asStateRecord, isStateRecord, type LegacyDispatch, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { isSameCommand } from 'Utilities/Command';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { hideMessage, showMessage } from './appActions';
import { removeItem, set, updateItem } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';
import { requestLegacy } from './Creators/legacyRequest';
import { asPromise } from './Creators/operationalRequests';
import { positiveInteger } from './Creators/pagedCollection';

export const section = 'commands';

interface CommandRecord extends StateRecord {
  id: number;
  name: string;
  status: string;
}

interface CommandOperations {
  lastCommand?: StateRecord;
  lastCommandTimeout?: ReturnType<typeof setTimeout>;
  removeTimeouts: Map<number, ReturnType<typeof setTimeout>>;
  callbacks: Map<number, (command: CommandRecord) => unknown>;
}

const stores = new WeakMap<LegacyDispatch, CommandOperations>();
function operations(dispatch: LegacyDispatch): CommandOperations {
  const existing = stores.get(dispatch);
  if (existing) {
    return existing;
  }
  const created: CommandOperations = { removeTimeouts: new Map(), callbacks: new Map() };
  stores.set(dispatch, created);
  return created;
}

export function parseCommand(value: unknown): CommandRecord | null {
  const id = positiveInteger(asStateRecord(value).id);
  return isStateRecord(value) && id !== undefined && typeof value.name === 'string' &&
    typeof value.status === 'string' ?
    { ...value, id, name: value.name, status: value.status } : null;
}

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  items: [],
  handlers: {}
};

export const FETCH_COMMANDS = 'commands/fetchCommands';
export const EXECUTE_COMMAND = 'commands/executeCommand';
export const CANCEL_COMMAND = 'commands/cancelCommand';
export const ADD_COMMAND = 'commands/addCommand';
export const UPDATE_COMMAND = 'commands/updateCommand';
export const FINISH_COMMAND = 'commands/finishCommand';
export const REMOVE_COMMAND = 'commands/removeCommand';

export const fetchCommands = createThunk(FETCH_COMMANDS);
export const executeCommand = createThunk(EXECUTE_COMMAND);
export const cancelCommand = createThunk(CANCEL_COMMAND);
export const addCommand = createThunk(ADD_COMMAND);
export const updateCommand = createThunk(UPDATE_COMMAND);
export const finishCommand = createThunk(FINISH_COMMAND);
export const removeCommand = createThunk(REMOVE_COMMAND);

function showCommandMessage(command: CommandRecord, dispatch: LegacyDispatch) {
  const { id, name, trigger, message, status } = command;
  const body = asStateRecord(command.body);
  if (typeof message !== 'string' || !message || body.sendUpdatesToClient !== true || body.suppressMessages) {
    return;
  }

  let type = messageTypes.INFO;
  let hideAfter = 0;
  if (status === 'completed') {
    type = messageTypes.SUCCESS;
    hideAfter = 4;
  } else if (status === 'failed') {
    type = messageTypes.ERROR;
    hideAfter = trigger === 'manual' ? 10 : 4;
  }
  dispatch(showMessage({ id, name, message, type, hideAfter }));
}

function scheduleRemoveCommand(command: CommandRecord, dispatch: LegacyDispatch) {
  const { id, status } = command;
  if (status === 'queued') {
    return;
  }

  const store = operations(dispatch);
  const timeout = store.removeTimeouts.get(id);
  if (timeout !== undefined) {
    clearTimeout(timeout);
  }
  store.removeTimeouts.set(id, setTimeout(() => {
    dispatch(batchActions([
      removeItem({ section, id }),
      hideMessage({ id })
    ]));
    store.removeTimeouts.delete(id);
    store.callbacks.delete(id);
  }, 60000 * 5));
}

export async function executeCommandHelper(payload: StateRecord, dispatch: LegacyDispatch): Promise<CommandRecord> {
  const { commandFinished, ...requestPayload } = payload;
  if (typeof requestPayload.name !== 'string' || !requestPayload.name.trim()) {
    throw new ApiError({ kind: 'invalid-request' });
  }
  const store = operations(dispatch);
  if (store.lastCommand && isSameCommand(store.lastCommand, requestPayload)) {
    console.warn('Please wait at least 5 seconds before running this command again');
  }
  store.lastCommand = requestPayload;
  if (store.lastCommandTimeout !== undefined) {
    clearTimeout(store.lastCommandTimeout);
  }
  store.lastCommandTimeout = setTimeout(() => {
    store.lastCommand = undefined;
    store.lastCommandTimeout = undefined;
  }, 5000);

  const { request } = requestLegacy({
    url: '/command',
    method: 'POST',
    data: JSON.stringify(requestPayload),
    dataType: 'json'
  });
  const command = parseCommand(await asPromise(request));
  if (!command) {
    throw new ApiError({ kind: 'invalid-response' });
  }
  if (typeof commandFinished === 'function') {
    store.callbacks.set(command.id, (result) => commandFinished(result));
  }
  dispatch(addCommand(command));
  // Callers such as the calendar need the actual response identity.
  return command;
}

export const actionHandlers = handleThunks({
  [FETCH_COMMANDS]: createFetchHandler(section, '/command'),
  [EXECUTE_COMMAND]: function(_getState, payload, dispatch) {
    return executeCommandHelper(payload, dispatch).catch((error: unknown) => dispatch(set({ section, error })));
  },
  [CANCEL_COMMAND]: createRemoveItemHandler(section, '/command'),
  [ADD_COMMAND]: function(_getState, payload, dispatch) {
    const command = parseCommand(payload);
    if (command) {
      dispatch(updateItem({ ...command, section }));
    }
  },
  [UPDATE_COMMAND]: function(_getState, payload, dispatch) {
    const command = parseCommand(payload);
    if (command) {
      dispatch(updateItem({ ...command, section }));
      showCommandMessage(command, dispatch);
      scheduleRemoveCommand(command, dispatch);
    }
  },
  [FINISH_COMMAND]: function(getState, payload, dispatch) {
    const command = parseCommand(payload);
    if (!command) {
      return;
    }
    const handlers = asStateRecord(getState().commands.handlers);
    Object.values(handlers).forEach((entry) => {
      const handler = asStateRecord(entry);
      if (handler.name === command.name && typeof handler.handler === 'function') {
        const action: unknown = handler.handler(command);
        if (typeof action === 'function') {
          dispatch((next, state) => action(next, state));
        } else if (isStateRecord(action) && typeof action.type === 'string') {
          dispatch({ ...action, type: action.type });
        }
      }
    });
    const callbacks = operations(dispatch).callbacks;
    const finished = callbacks.get(command.id);
    callbacks.delete(command.id);
    finished?.(command);
    dispatch(updateItem({ ...command, section }));
    scheduleRemoveCommand(command, dispatch);
    showCommandMessage(command, dispatch);
  },
  [REMOVE_COMMAND]: function(_getState, payload, dispatch) {
    const id = positiveInteger(payload.id);
    if (id !== undefined) {
      const store = operations(dispatch);
      const timeout = store.removeTimeouts.get(id);
      if (timeout !== undefined) {
        clearTimeout(timeout);
      }
      store.removeTimeouts.delete(id);
      store.callbacks.delete(id);
      dispatch(removeItem({ section, id }));
    }
  }
});

export const reducers = createHandleActions({}, defaultState, section);
