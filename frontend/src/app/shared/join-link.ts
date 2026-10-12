import { MatSnackBar } from '@angular/material/snack-bar';

import { copyText } from './clipboard';

export const joinLinkUrl = (token: string): string => `${window.location.origin}/join/${token}`;

export function copyJoinLink(snackBar: MatSnackBar, token: string): void {
  copyText(snackBar, joinLinkUrl(token), 'Link copied');
}
