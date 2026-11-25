import { createContext } from 'react';
import type { FieldAssociation } from './formTypes';

export default createContext<FieldAssociation | null>(null);
