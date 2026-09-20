import { MatSnackBar } from '@angular/material/snack-bar';

export const joinLinkUrl = (token: string): string => `${window.location.origin}/join/${token}`;

export function copyJoinLink(snackBar: MatSnackBar, token: string): void {
  const url = joinLinkUrl(token);
  navigator.clipboard.writeText(url).then(
    () => snackBar.open('Link copied', 'Dismiss', { duration: 2000 }),
    () => snackBar.open(url, 'Dismiss'),
  );
}
