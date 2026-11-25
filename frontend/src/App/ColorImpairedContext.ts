import React from 'react';

const ColorImpairedContext = React.createContext<boolean>(false);
export const ColorImpairedConsumer = ColorImpairedContext.Consumer;

export default ColorImpairedContext;
