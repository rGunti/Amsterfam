import { Component, OnInit, inject, signal } from '@angular/core';
import { MAT_BOTTOM_SHEET_DATA } from '@angular/material/bottom-sheet';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable } from 'rxjs';

import { NewsApi } from '../../core/api/news.api';
import { NewsFeedTokenStatus } from '../../core/models/news';
import { copyText } from '../../shared/clipboard';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { fullTimestamp } from '../../shared/timestamp';

export interface NewsFeedSheetData {
  eventId: string;
}

/**
 * Bottom sheet behind the News page's "Subscribe via RSS" button (ADR-016). The feed link is
 * only known right after it's created; afterwards it can be regenerated or revoked.
 */
@Component({
  selector: 'app-news-feed-sheet',
  imports: [MatButtonModule, MatIconModule],
  template: `
    <div class="sheet">
      <h2><mat-icon>rss_feed</mat-icon> Subscribe via RSS</h2>
      @if (error()) {
        <p class="note">Couldn't load your feed link. Try again later.</p>
      } @else if (url(); as u) {
        <p class="note">
          Add this link to your feed reader. It's only shown now, so copy it before closing.
        </p>
        <code class="url">{{ u }}</code>
        <div class="actions">
          <button mat-flat-button (click)="copy(u)">
            <mat-icon>content_copy</mat-icon> Copy link
          </button>
          <button mat-button color="warn" (click)="revoke()" [disabled]="busy()">
            <mat-icon>link_off</mat-icon> Revoke
          </button>
        </div>
      } @else if (status(); as s) {
        <p class="note">
          Your own private link to this event's news, for a feed reader. Don't share it: anyone with
          the link can read the news. Readers that run online (like Feedly) keep a copy of the posts
          and images on their servers.
        </p>
        @if (s.active) {
          <p class="active">
            You have a feed link, created {{ timestamp(s.createdAt!) }}.
            @if (s.lastUsedAt) {
              Last used {{ timestamp(s.lastUsedAt) }}.
            } @else {
              Not used yet.
            }
          </p>
          <div class="actions">
            <button mat-stroked-button (click)="regenerate()" [disabled]="busy()">
              <mat-icon>autorenew</mat-icon> Get a new link
            </button>
            <button mat-button color="warn" (click)="revoke()" [disabled]="busy()">
              <mat-icon>link_off</mat-icon> Revoke
            </button>
          </div>
        } @else {
          <div class="actions">
            <button mat-flat-button (click)="create()" [disabled]="busy()">
              <mat-icon>add_link</mat-icon> Create feed link
            </button>
          </div>
        }
      } @else {
        <p class="note">Loading…</p>
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
    .note,
    .active {
      margin: 0;
    }
    .note {
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
export class NewsFeedSheet implements OnInit {
  readonly data = inject<NewsFeedSheetData>(MAT_BOTTOM_SHEET_DATA);
  private readonly api = inject(NewsApi);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly status = signal<NewsFeedTokenStatus | null>(null);
  /** The link, only while it's freshly created. */
  readonly url = signal<string | null>(null);
  readonly error = signal(false);
  readonly busy = signal(false);

  ngOnInit(): void {
    this.api.feedToken(this.data.eventId).subscribe({
      next: (status) => this.status.set(status),
      error: () => this.error.set(true),
    });
  }

  timestamp(iso: string): string {
    return fullTimestamp(iso);
  }

  copy(url: string): void {
    copyText(this.snackBar, url, 'Link copied');
  }

  create(): void {
    this.busy.set(true);
    this.api.createFeedToken(this.data.eventId).subscribe({
      next: (created) => {
        this.busy.set(false);
        this.url.set(created.url);
        this.status.set({ active: true, createdAt: created.createdAt, lastUsedAt: null });
      },
      error: () => {
        this.busy.set(false);
        this.snackBar.open('Could not create the link', 'Dismiss', { duration: 3000 });
      },
    });
  }

  regenerate(): void {
    this.confirm({
      title: 'Get a new feed link',
      message:
        'Your current feed link stops working right away. Update your feed reader with the new one.',
      confirmLabel: 'Get new link',
    }).subscribe((ok) => {
      if (ok) {
        this.create();
      }
    });
  }

  revoke(): void {
    this.confirm({
      title: 'Revoke feed link',
      message:
        'Your feed reader stops getting news for this event. You can create a new link later.',
      confirmLabel: 'Revoke',
    }).subscribe((ok) => {
      if (!ok) {
        return;
      }
      this.busy.set(true);
      this.api.revokeFeedToken(this.data.eventId).subscribe({
        next: () => {
          this.busy.set(false);
          this.url.set(null);
          this.status.set({ active: false, createdAt: null, lastUsedAt: null });
          this.snackBar.open('Feed link revoked', 'Dismiss', { duration: 2000 });
        },
        error: () => {
          this.busy.set(false);
          this.snackBar.open('Could not revoke the link', 'Dismiss', { duration: 3000 });
        },
      });
    });
  }

  private confirm(data: ConfirmDialogData): Observable<boolean | undefined> {
    return this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed();
  }
}
