import { useDispatch } from 'react-redux';
import type { LegacyDispatch } from './StateTypes';

export default useDispatch.withTypes<LegacyDispatch>();
