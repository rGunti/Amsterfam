import { MatSnackBar } from '@angular/material/snack-bar';

/** Copies `text`, or shows it in a snackbar to copy by hand when the clipboard isn't available. */
export function copyText(snackBar: MatSnackBar, text: string, doneMessage = 'Copied'): void {
  navigator.clipboard.writeText(text).then(
    () => snackBar.open(doneMessage, 'Dismiss', { duration: 2000 }),
    () => snackBar.open(text, 'Dismiss'),
  );
}
