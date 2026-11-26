import { type AriaAttributes,useCallback, useMemo } from 'react';
import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import EnhancedSelectInput from './EnhancedSelectInput';
import type { ValueChange } from './formTypes';

const selectIndexerFlagsValues = (selectedFlags: number) =>
  createSelector(
    (state: AppState) => state.settings.indexerFlags,
    (indexerFlags) => {
      const value = indexerFlags.items.reduce((acc: number[], { id }) => {
        // eslint-disable-next-line no-bitwise
        if ((selectedFlags & id) === id) {
          acc.push(id);
        }

        return acc;
      }, []);

      const values = indexerFlags.items.map(({ id, name }) => ({
        key: id,
        value: name,
      }));

      return {
        value,
        values,
      };
    }
  );

// Only alter bits that the server's current flag catalog exposes.
export function updateIndexerFlags(previous: number, known: readonly number[], selected: readonly number[]): number {
  /* eslint-disable no-bitwise */
  const mask = known.reduce((flags, id) => flags | id, 0);
  const enabled = selected.reduce((flags, id) => flags | id, 0) & mask;
  return (previous & ~mask) | enabled;
  /* eslint-enable no-bitwise */
}

interface IndexerFlagsSelectInputProps extends AriaAttributes {
  id?: string;
  name: string;
  indexerFlags: number;
  onChange(payload: ValueChange<number>): void;
}

function IndexerFlagsSelectInput(props: IndexerFlagsSelectInputProps) {
  const { indexerFlags, onChange } = props;

  const selector = useMemo(() => selectIndexerFlagsValues(indexerFlags), [indexerFlags]);
  const { value, values } = useSelector(selector);

  const onChangeWrapper = useCallback(
    ({ name, value }: { name: string; value: number[] }) => {
      onChange({ name, value: updateIndexerFlags(indexerFlags, values.map((option) => option.key), value) });
    },
    [indexerFlags, onChange, values]
  );

  return (
    <EnhancedSelectInput<number>
      {...props}
      value={value}
      values={values}
      onChange={onChangeWrapper}
    />
  );
}

export default IndexerFlagsSelectInput;
