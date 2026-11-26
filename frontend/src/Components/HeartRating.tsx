import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import styles from './HeartRating.css';

export interface HeartRatingProps {
  rating: number;
  iconSize?: number;
}

export default function HeartRating({ rating, iconSize = 14 }: HeartRatingProps) {
  return <span className={styles.rating}><Icon className={styles.heart} name={icons.HEART} size={iconSize} />{rating.toFixed(1)}</span>;
}
