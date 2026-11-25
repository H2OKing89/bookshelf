import type { ModalProps } from 'Components/Modal/Modal';
import type { TableSelectCellProps } from 'Components/Table/Cells/TableSelectCell';
import type { VirtualTableSelectCellProps } from 'Components/Table/Cells/VirtualTableSelectCell';
import type { TablePagerProps } from 'Components/Table/TablePager';
import type { TableSelectionChange } from 'Components/Table/tableSelection';
import { sizes } from 'Helpers/Props';

const modal: ModalProps = { isOpen: false, size: sizes.LARGE, onModalClose: () => { /* callback */ } };
const selection: TableSelectionChange<string> = { id: 'recording:1', value: null };
const row: TableSelectCellProps<number> = { id: 0, onSelectedChange: ({ value }) => { const selected: boolean | null = value; return selected; } };
// @ts-expect-error: a closed modal has a boolean flag, not a saved string.
const invalidModal: ModalProps = { isOpen: 'false', onModalClose: () => { /* callback */ } };
// @ts-expect-error: a virtual row ID cannot be converted into a numeric selection handler.
const invalidVirtual: VirtualTableSelectCellProps<string> = { id: 'queue:1', onSelectedChange: (event: { id: number; value: boolean }) => event.id };
// @ts-expect-error: page selection must be numeric (native select is decoded at the boundary).
const invalidPager: TablePagerProps['onPageSelect'] = (page: string) => page.length;

export const typedContracts = [modal, selection, row, invalidModal, invalidVirtual, invalidPager];
