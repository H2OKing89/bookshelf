export default function formControlProps(props) {
  return {
    id: props.id,
    'aria-label': props['aria-label'],
    'aria-labelledby': props['aria-labelledby'],
    'aria-describedby': props['aria-describedby'],
    'aria-invalid': props['aria-invalid']
  };
}
