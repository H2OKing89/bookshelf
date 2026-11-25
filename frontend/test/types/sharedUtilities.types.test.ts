import formatDate from 'Utilities/Date/formatDate';
import convertToBytes from 'Utilities/Number/convertToBytes';
import getRemovedItems from 'Utilities/Object/getRemovedItems';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import toggleSelected from 'Utilities/Table/toggleSelected';

const byId = getRemovedItems([{ id: 7, name: 'A' }], [{ id: 8, name: 'B' }]);
const title: string = byId[0].name;
const recording = getRemovedItems([{ recordingId: 'local:recording:x' }], [], 'recordingId');
const opaque: string[] = getSelectedIds({ 'queue:client:owned': true }, { parseIds: false });
const numeric: number[] = getSelectedIds({ '12': true });
const size: number | '' = convertToBytes('0', 1, true);
const formatted: string = formatDate(undefined, 'YYYY-MM-DD');

// @ts-expect-error: a collection identity property must exist on the actual item.
getRemovedItems([{ id: 1 }], [{ id: 2 }], 'foreignId');
// @ts-expect-error: opaque queue identities are not numeric selection IDs.
const wrongIds: number[] = getSelectedIds({ 'queue:client:owned': true }, { parseIds: false });
// @ts-expect-error: selected flags have a boolean/null contract, not a string value.
getSelectedIds({ '1': 'true' });
// @ts-expect-error: offsets use numeric exponent values, not arbitrary strings.
convertToBytes('1', '2', true);
// @ts-expect-error: selection range keys cannot be arbitrary objects.
toggleSelected({ selectedState: {} }, [{ id: { id: 1 } }], { id: 1 }, true, true);
export const typeAssertions = [title, recording, opaque, numeric, size, formatted, wrongIds];
