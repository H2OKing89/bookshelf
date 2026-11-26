import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import { editionOptions, selectEdition } from './providerSelectOptions';
import SelectInput from './SelectInput';

interface OwnProps extends FormControlProps {
  name: string;
  bookEditions?: unknown;
  onChange: (change: ValueChange<unknown[]>) => void;
}

function createMapStateToProps() {
  return createSelector(
    (_state: unknown, props: OwnProps) => props.bookEditions,
    editionOptions
  );
}

const connector = connect(createMapStateToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class BookEditionSelectInputConnector extends Component<Props> {
  onChange = ({ name, value }: ValueChange<string>) => {
    const editions = selectEdition(this.props.bookEditions, value);
    if (editions) {
      this.props.onChange({ name, value: editions });
    }
  };

  render() {
    return <SelectInput {...this.props} onChange={this.onChange} />;
  }
}

export default connector(BookEditionSelectInputConnector);
