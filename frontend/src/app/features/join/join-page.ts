import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';

import { JoinLinkApi } from '../../core/api/join-link.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { JoinLinkPreviewResponse } from '../../core/models/join-link';
import { isCompactScreen } from '../../shared/compact-screen';
import { EventBanner } from '../../shared/event-banner/event-banner';
import { OrganiserAvatarStack } from '../../shared/organiser-avatar-stack/organiser-avatar-stack';

@Component({
  selector: 'app-join-page',
  imports: [
    RouterLink,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    EventBanner,
    OrganiserAvatarStack,
  ],
  template: `
    <mat-card class="join-card" [class.has-banner]="!!preview()?.bannerFileId">
      @if (loading()) {
        <mat-card-content><p>Checking invite…</p></mat-card-content>
      } @else if (preview(); as p) {
        @if (p.bannerFileId; as bannerVersion) {
          <app-event-banner [src]="bannerUrl" [version]="bannerVersion">
            <div class="banner-title">You're invited to {{ p.eventName }}</div>
            @if (p.startDate && p.endDate) {
              <p class="banner-meta">
                <mat-icon inline>event</mat-icon>
                {{ p.startDate | date: 'mediumDate' }} – {{ p.endDate | date: 'mediumDate' }}
              </p>
            }
            <p class="banner-meta"><mat-icon inline>place</mat-icon> {{ p.location }}</p>
            @if (!compact()) {
              <app-organiser-avatar-stack
                bannerAside
                class="banner-organisers"
                [organisers]="p.organisers"
                [ownerId]="p.ownerId"
              />
            }
          </app-event-banner>
        } @else {
          <mat-card-header>
            <mat-card-title>You're invited to {{ p.eventName }}</mat-card-title>
            <mat-card-subtitle>{{ p.location }}</mat-card-subtitle>
          </mat-card-header>
        }
        <mat-card-content>
          @if ((compact() || !p.bannerFileId) && p.organisers.length > 0) {
            <app-organiser-avatar-stack
              class="organisers"
              [organisers]="p.organisers"
              [ownerId]="p.ownerId"
            />
          }
          @if (!p.bannerFileId && p.startDate && p.endDate) {
            <p>{{ p.startDate | date: 'mediumDate' }} – {{ p.endDate | date: 'mediumDate' }}</p>
          }
          @if (p.kind === 'Organiser') {
            <p class="note">
              <mat-icon>admin_panel_settings</mat-icon>
              <span>This link is for organisers. The event owner will need to approve you.</span>
            </p>
          } @else {
            <p>An organiser will need to approve your request before you're in.</p>
          }
        </mat-card-content>
        <mat-card-actions>
          @if (p.alreadyMember) {
            <a mat-flat-button color="primary" [routerLink]="['/events', p.eventId]">
              Go to event
            </a>
          } @else {
            <button mat-flat-button color="primary" (click)="join()" [disabled]="joining()">
              <mat-icon>person_add</mat-icon> Request to join
            </button>
          }
        </mat-card-actions>
      } @else {
        <mat-card-header>
          <mat-card-title>This link doesn't work</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <p>
            The invite may have expired, been revoked or reached its limit. Ask an organiser for a
            new one.
          </p>
        </mat-card-content>
        <mat-card-actions>
          <a mat-button routerLink="/">Back to my events</a>
        </mat-card-actions>
      }
    </mat-card>
  `,
  styles: `
    :host {
      display: block;
      padding: 16px;
    }
    .join-card {
      max-width: 480px;
      margin: 0 auto;
    }
    // On phones the banner runs edge to edge: cancel this host's padding and the page's.
    @media (max-width: 599.98px) {
      .join-card.has-banner {
        margin: calc(-1 * (var(--page-padding, 24px) + 16px));
        margin-bottom: 0;
        max-width: none;
        border-radius: 0;
      }
    }
    .banner-title {
      font: var(--mat-sys-headline-small);
      text-shadow: 0 1px 3px rgb(0 0 0 / 50%);
    }
    .banner-organisers {
      --organiser-label-color: #fff;
      display: block;
      text-shadow: 0 1px 3px rgb(0 0 0 / 50%);
    }
    .organisers {
      display: block;
      margin: 12px 0;
    }
    .banner-meta {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 4px 0 0;
      text-shadow: 0 1px 3px rgb(0 0 0 / 50%);
    }
    .note {
      display: flex;
      align-items: center;
      gap: 12px;
      margin: 12px 0 0;
    }
    .note mat-icon {
      flex: none;
    }
    mat-card-actions {
      padding: 16px 16px 16px;
    }
  `,
})
export class JoinPage implements OnInit {
  private readonly api = inject(JoinLinkApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly currentEventService = inject(CurrentEventService);
  private readonly token = inject(ActivatedRoute).snapshot.paramMap.get('token') ?? '';

  readonly compact = isCompactScreen();
  readonly bannerUrl = this.api.bannerUrl(this.token);
  readonly loading = signal(true);
  readonly joining = signal(false);
  readonly preview = signal<JoinLinkPreviewResponse | null>(null);

  ngOnInit(): void {
    this.currentEventService.clear();
    this.api.preview(this.token).subscribe({
      next: (p) => {
        this.preview.set(p);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  join(): void {
    this.joining.set(true);
    this.api.join(this.token).subscribe({
      next: (res) => {
        this.snackBar.open('Request sent — waiting for approval', 'Dismiss', { duration: 3000 });
        void this.router.navigate(['/events', res.eventId]);
      },
      error: (err: HttpErrorResponse) => {
        this.joining.set(false);
        const message = err.error?.error ?? 'Could not join event';
        this.snackBar.open(message, 'Dismiss', { duration: 3000 });
      },
    });
  }
}
