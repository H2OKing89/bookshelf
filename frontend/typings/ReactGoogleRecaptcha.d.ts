declare module 'react-google-recaptcha' {
  import type { ComponentType } from 'react';

  interface ReCaptchaProps {
    sitekey: string;
    stoken?: string;
    onChange?: (response: string | null) => void;
  }

  const ReCAPTCHA: ComponentType<ReCaptchaProps>;
  export default ReCAPTCHA;
}
