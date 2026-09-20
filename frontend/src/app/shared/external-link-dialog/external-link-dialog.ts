import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

export interface ExternalLinkDialogData {
  /** Absolute http(s) URL the user is about to visit. */
  href: string;
}

/**
 * Warns before leaving the app through a link somebody else wrote. The "Open link" button is
 * a real anchor, so the browser handles the navigation and popup blockers stay out of it.
 */
@Component({
  selector: 'app-external-link-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Open external link?</h2>
    <mat-dialog-content>
      <p>
        You're about to leave Amsterfam and visit <strong>{{ host }}</strong
        >.
      </p>
      <p class="url">{{ data.href }}</p>
      <p class="warning">
        <mat-icon inline>warning</mat-icon>
        Only continue if you trust whoever added this link. Links can lead to fake login pages or
        harmful sites, and we can't vouch for what's on the other side.
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <a
        mat-flat-button
        [href]="data.href"
        target="_blank"
        rel="noopener noreferrer nofollow"
        (click)="dialogRef.close()"
      >
        Open link <mat-icon iconPositionEnd>open_in_new</mat-icon>
      </a>
    </mat-dialog-actions>
  `,
  styles: `
    .url {
      font-family: monospace;
      font-size: 0.875rem;
      word-break: break-all;
      color: var(--mat-sys-on-surface-variant);
    }
    .warning {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      font-size: 0.875rem;
    }
    .warning mat-icon {
      flex: none;
      margin-top: 2px;
    }
  `,
})
export class ExternalLinkDialog {
  readonly dialogRef = inject(MatDialogRef<ExternalLinkDialog>);
  readonly data = inject<ExternalLinkDialogData>(MAT_DIALOG_DATA);
  readonly host = new URL(this.data.href).host;
}
