const resolutions = { desktopLarge: 1200, desktop: 992, tablet: 768, mobile: 480 };

// jQuery(window).width() reads the layout viewport (documentElement.clientWidth).
// Preserve that width rather than including scrollbar width in innerWidth.
const viewportWidth = () => document.documentElement.clientWidth;

export default {
  resolutions,
  isDesktopLarge: () => viewportWidth() < resolutions.desktopLarge,
  isDesktop: () => viewportWidth() < resolutions.desktop,
  isTablet: () => viewportWidth() < resolutions.tablet,
  isMobile: () => viewportWidth() < resolutions.mobile
};
