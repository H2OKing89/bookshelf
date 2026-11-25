import type { IconProps } from 'Components/Icon';
import type { SpinnerButtonProps } from 'Components/Link/SpinnerButton';
import type { SpinnerIconButtonProps } from 'Components/Link/SpinnerIconButton';
import type { ProgressBarProps } from 'Components/ProgressBar';
import { icons, kinds, sizes } from 'Helpers/Props';

const icon: IconProps = { name: icons.CHECK, kind: kinds.SUCCESS, size: 12 };
const busy: SpinnerIconButtonProps = { name: icons.REFRESH, isSpinning: true, 'aria-label': 'Refresh', isDisabled: false };
const progress: ProgressBarProps = { progress: 0, size: sizes.MEDIUM, showText: false };
// @ts-expect-error: icon presentation kind is a closed set of theme tokens.
const invalidKind: IconProps = { name: icons.CHECK, kind: 'green' };
// @ts-expect-error: progress data must be numeric, not preformatted.
const invalidProgress: ProgressBarProps = { progress: '45%' };
// @ts-expect-error: a spinner button requires an explicit busy flag.
const missingBusy: SpinnerButtonProps = { children: 'Save' };

export const controlTypes = [icon, busy, progress, invalidKind, invalidProgress, missingBusy];
