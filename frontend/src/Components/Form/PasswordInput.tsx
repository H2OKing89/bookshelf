import React from 'react';
import TextInput, { type TextInputProps } from './TextInput';

// Prevent a user from copying (or cutting) the password from the input
function onCopy(e: React.ClipboardEvent<HTMLInputElement>) {
  e.preventDefault();
  e.nativeEvent.stopImmediatePropagation();
}

function PasswordInput(props: Omit<TextInputProps, 'type' | 'onCopy'>) {
  return (
    <TextInput
      {...props}
      type="password"
      onCopy={onCopy}
    />
  );
}

export default PasswordInput;
