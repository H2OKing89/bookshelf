import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';

interface PortalProps {
  children: ReactNode;
  target?: Element | DocumentFragment | null;
}

function Portal({ children, target = document.getElementById('portal-root') }: PortalProps) {
  return target ? createPortal(children, target) : null;
}

export default Portal;
