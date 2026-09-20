import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MAT_BOTTOM_SHEET_DATA, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';

import { JoinLinkApi } from '../../core/api/join-link.api';
import { JoinLinkKind, JoinLinkResponse } from '../../core/models/join-link';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { copyJoinLink, joinLinkUrl } from '../../shared/join-link';

export interface JoinLinkShareSheetData {
  eventId: string;
  eventName: string;
  /** Drafts share an organiser link, everything else an attendee link. */
  kind: JoinLinkKind;
}

/**
 * Bottom sheet behind the overview's share button. Shows the first valid link of the
 * wanted kind and creates one if there is none yet.
 */
@Component({
  selector: 'app-join-link-share-sheet',
  imports: [MatButtonModule, MatIconModule],
  template: `
    <div class="sheet">
      <h2>
        <mat-icon>{{ data.kind === 'Organiser' ? 'admin_panel_settings' : 'person_add' }}</mat-icon>
        Invite to {{ data.eventName }}
      </h2>
      @if (error()) {
        <p>Couldn't load a join link. Try again from the Join links page.</p>
      } @else if (link(); as l) {
        <p class="note">
          {{
            data.kind === 'Organiser'
              ? 'Organiser link — people who use it can become organisers once you approve them.'
              : 'Anyone with this link can ask to join; an organiser still has to approve them.'
          }}
        </p>
        <code class="url">{{ url(l) }}</code>
        <div class="actions">
          <button mat-flat-button color="primary" (click)="copy(l)">
            <mat-icon>content_copy</mat-icon> Copy link
          </button>
          @if (canShare) {
            <button mat-stroked-button (click)="share(l)"><mat-icon>share</mat-icon> Share…</button>
          }
          <button mat-button (click)="manage()">Manage links</button>
          <button mat-button color="warn" (click)="regenerate(l)" [disabled]="regenerating()">
            <mat-icon>autorenew</mat-icon> Regenerate this link
          </button>
        </div>
      } @else {
        <p class="note">Getting a link…</p>
      }
    </div>
  `,
  styles: `
    .sheet {
      display: flex;
      flex-direction: column;
      gap: 12px;
      padding: 8px 8px 16px;
    }
    h2 {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0;
    }
    .note {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
    .url {
      padding: 8px 12px;
      border-radius: 8px;
      background: var(--mat-sys-surface-container-highest);
      overflow-wrap: anywhere;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
  `,
})
export class JoinLinkShareSheet implements OnInit {
  readonly data = inject<JoinLinkShareSheetData>(MAT_BOTTOM_SHEET_DATA);
  private readonly ref = inject<MatBottomSheetRef<JoinLinkShareSheet>>(MatBottomSheetRef);
  private readonly api = inject(JoinLinkApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly link = signal<JoinLinkResponse | null>(null);
  readonly error = signal(false);
  readonly regenerating = signal(false);
  readonly canShare = typeof navigator !== 'undefined' && typeof navigator.share === 'function';

  ngOnInit(): void {
    const { eventId, kind } = this.data;
    this.api.list(eventId).subscribe({
      next: (links) => {
        const existing = links.find((l) => l.kind === kind && l.isUsable);
        if (existing) {
          this.link.set(existing);
          return;
        }
        this.api.create(eventId, { kind, label: null, expiresAt: null, maxUses: null }).subscribe({
          next: (created) => this.link.set(created),
          error: () => this.error.set(true),
        });
      },
      error: () => this.error.set(true),
    });
  }

  url(link: JoinLinkResponse): string {
    return joinLinkUrl(link.token);
  }

  copy(link: JoinLinkResponse): void {
    copyJoinLink(this.snackBar, link.token);
  }

  share(link: JoinLinkResponse): void {
    navigator.share({ title: this.data.eventName, url: this.url(link) }).catch(() => undefined); // dismissed by the user
  }

  regenerate(link: JoinLinkResponse): void {
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Regenerate this link',
          message:
            'The current link stops working right away and a new one with the same settings replaces it. Anyone still holding the old link will need the new one.',
          confirmLabel: 'Regenerate',
        },
      })
      .afterClosed()
      .subscribe((ok) => {
        if (!ok) {
          return;
        }
        this.regenerating.set(true);
        this.api.regenerate(this.data.eventId, link.id).subscribe({
          next: (fresh) => {
            this.regenerating.set(false);
            this.link.set(fresh);
            this.snackBar.open('New link created', 'Dismiss', { duration: 2000 });
          },
          error: () => {
            this.regenerating.set(false);
            this.snackBar.open('Could not regenerate the link', 'Dismiss', { duration: 3000 });
          },
        });
      });
  }

  manage(): void {
    this.ref.dismiss();
    void this.router.navigate(['/events', this.data.eventId, 'join-links']);
  }
}
