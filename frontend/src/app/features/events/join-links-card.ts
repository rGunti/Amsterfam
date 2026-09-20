import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';

import { JoinLinkApi } from '../../core/api/join-link.api';
import { JoinLinkKind, JoinLinkResponse } from '../../core/models/join-link';

@Component({
  selector: 'app-join-links-card',
  imports: [
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  template: `
    <mat-card class="event-card">
      <mat-card-header>
        <mat-card-title>Join links</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <p class="detail-row">
          Share a link to let people request to join. Everyone still needs to be approved.
        </p>

        <div class="create-row">
          @if (isOwner()) {
            <mat-form-field>
              <mat-label>For</mat-label>
              <mat-select [(ngModel)]="kind">
                <mat-option value="Attendee">Attendees</mat-option>
                <mat-option value="Organiser">Organisers</mat-option>
              </mat-select>
            </mat-form-field>
          }
          <mat-form-field>
            <mat-label>Expires (optional)</mat-label>
            <input matInput type="date" [min]="today" [(ngModel)]="expires" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>Max uses (optional)</mat-label>
            <input matInput type="number" min="1" [(ngModel)]="maxUses" />
          </mat-form-field>
          <button mat-flat-button color="primary" (click)="create()" [disabled]="busy()">
            <mat-icon>add_link</mat-icon> Create link
          </button>
        </div>

        @if (links().length === 0) {
          <p class="detail-row">No active links.</p>
        }
        @for (link of links(); track link.id) {
          <div class="link-row">
            <div>
              <strong>{{ link.kind === 'Organiser' ? 'Organiser link' : 'Attendee link' }}</strong>
              <div class="detail-row">
                Used {{ link.useCount }}{{ link.maxUses ? ' / ' + link.maxUses : '' }} ·
                @if (link.expiresAt) {
                  {{ link.isUsable ? 'expires' : 'expired' }}
                  {{ link.expiresAt | date: 'medium' }}
                } @else {
                  never expires
                }
              </div>
            </div>
            <span>
              <button mat-button (click)="copy(link)" [disabled]="!link.isUsable">
                <mat-icon>content_copy</mat-icon> Copy
              </button>
              <button mat-button color="warn" (click)="revoke(link)" [disabled]="busy()">
                <mat-icon>link_off</mat-icon> Revoke
              </button>
            </span>
          </div>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .create-row {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;
    }
    .link-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 8px;
      padding: 8px 0;
    }
  `,
})
export class JoinLinksCard implements OnInit {
  private readonly api = inject(JoinLinkApi);
  private readonly snackBar = inject(MatSnackBar);

  readonly eventId = input.required<string>();
  readonly isOwner = input(false);

  readonly links = signal<JoinLinkResponse[]>([]);
  readonly busy = signal(false);
  readonly today = new Date().toISOString().slice(0, 10);
  kind: JoinLinkKind = 'Attendee';
  expires = '';
  maxUses: number | null = null;

  private readonly activeKind = computed(() => (this.isOwner() ? this.kind : 'Attendee'));

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.api.list(this.eventId()).subscribe((links) => this.links.set(links));
  }

  create(): void {
    this.busy.set(true);
    this.api
      .create(this.eventId(), {
        kind: this.activeKind(),
        // Expire at the end of the chosen local day.
        expiresAt: this.expires ? new Date(`${this.expires}T23:59:59`).toISOString() : null,
        maxUses: this.maxUses || null,
      })
      .subscribe({
        next: (link) => {
          this.busy.set(false);
          this.expires = '';
          this.maxUses = null;
          this.load();
          this.copy(link);
        },
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          this.snackBar.open(err.error?.error ?? 'Could not create link', 'Dismiss', {
            duration: 3000,
          });
        },
      });
  }

  revoke(link: JoinLinkResponse): void {
    this.busy.set(true);
    this.api.revoke(this.eventId(), link.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: () => {
        this.busy.set(false);
        this.snackBar.open('Could not revoke link', 'Dismiss', { duration: 3000 });
      },
    });
  }

  copy(link: JoinLinkResponse): void {
    const url = `${window.location.origin}/join/${link.token}`;
    navigator.clipboard.writeText(url).then(
      () => this.snackBar.open('Link copied', 'Dismiss', { duration: 2000 }),
      () => this.snackBar.open(url, 'Dismiss'),
    );
  }
}
