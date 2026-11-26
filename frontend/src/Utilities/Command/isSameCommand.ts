import _ from 'lodash';
import { isStateRecord } from 'Store/StateTypes';

function isSameCommand(commandA: unknown, commandB: unknown): boolean {
  if (!isStateRecord(commandA) || !isStateRecord(commandB) ||
      typeof commandA.name !== 'string' || typeof commandB.name !== 'string' ||
      commandA.name.toLocaleLowerCase() !== commandB.name.toLocaleLowerCase()) {
    return false;
  }
  // Array constraints represent a requested subset, not equality of order.
  return Object.entries(commandB).every(([key, value]) => {
    if (key === 'name') {
      return true;
    }
    const actual = commandA[key];
    return Array.isArray(value) ? Array.isArray(actual) && _.difference(value, actual).length === 0 : value === actual;
  });
}
export default isSameCommand;
