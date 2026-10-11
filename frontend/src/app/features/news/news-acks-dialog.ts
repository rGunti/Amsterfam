import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

import { NewsApi } from '../../core/api/news.api';
import { NewsAcks } from '../../core/models/news';
import { TimelineUser } from '../../core/models/timeline';
import { fullTimestamp } from '../../shared/timestamp';

export interface NewsAcksDialogData {
  eventId: string;
  postId: number;
  title: string | null;
}

/** For organisers: who has said "Got it" to a post, and who hasn't yet. */
@Component({
  selector: 'app-news-acks-dialog',
  imports: [NgTemplateOutlet, MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title ? '“' + data.title + '”' : 'Acknowledgements' }}</h2>
    <mat-dialog-content>
      @if (loading()) {
        <p>Loading…</p>
      } @else if (acks(); as acks) {
        <h3>Not yet ({{ acks.missing.length }})</h3>
        @if (acks.missing.length === 0) {
          <p class="note">Everyone has acknowledged this post.</p>
        }
        @for (user of acks.missing; track user.id) {
          <div class="user-row">
            <ng-container *ngTemplateOutlet="avatar; context: { $implicit: user }" />
            <span>{{ user.displayName }}</span>
          </div>
        }

        <h3>Got it ({{ acks.acknowledged.length }})</h3>
        @if (acks.acknowledged.length === 0) {
          <p class="note">Nobody yet.</p>
        }
        @for (ack of acks.acknowledged; track ack.user.id) {
          <div class="user-row">
            <ng-container *ngTemplateOutlet="avatar; context: { $implicit: ack.user }" />
            <span>{{ ack.user.displayName }}</span>
            <span class="note when">{{ stamp(ack.at) }}</span>
          </div>
        }
      } @else {
        <p>Could not load the acknowledgements.</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Close</button>
    </mat-dialog-actions>

    <ng-template #avatar let-user>
      @if (user.avatarUrl) {
        <img class="avatar" [src]="user.avatarUrl" alt="" />
      } @else {
        <span class="avatar avatar-initial" aria-hidden="true">{{ initial(user) }}</span>
      }
    </ng-template>
  `,
  styles: `
    h3 {
      margin: 16px 0 4px;
      font: var(--mat-sys-title-small);
    }

    .user-row {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 6px 0;
    }

    .avatar {
      flex: none;
      width: 28px;
      height: 28px;
      border-radius: 50%;
      object-fit: cover;
    }

    .avatar-initial {
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
      font-weight: 500;
    }

    .note {
      color: var(--mat-sys-on-surface-variant);
    }

    .when {
      margin-left: auto;
      font-size: 12px;
    }
  `,
})
export class NewsAcksDialog implements OnInit {
  private readonly api = inject(NewsApi);
  readonly data = inject<NewsAcksDialogData>(MAT_DIALOG_DATA);

  readonly acks = signal<NewsAcks | null>(null);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.api.acks(this.data.eventId, this.data.postId).subscribe({
      next: (acks) => {
        this.acks.set(acks);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  stamp(iso: string): string {
    return fullTimestamp(iso);
  }

  initial(user: TimelineUser): string {
    return [...user.displayName.trim()][0]?.toUpperCase() ?? '?';
  }
}
