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

@Component({
  selector: 'app-join-page',
  imports: [RouterLink, DatePipe, MatButtonModule, MatCardModule, MatIconModule],
  template: `
    <mat-card class="join-card">
      @if (loading()) {
        <mat-card-content><p>Checking invite…</p></mat-card-content>
      } @else if (preview(); as p) {
        <mat-card-header>
          <mat-card-title>You're invited to {{ p.eventName }}</mat-card-title>
          <mat-card-subtitle>{{ p.location }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          @if (p.startDate && p.endDate) {
            <p>{{ p.startDate | date: 'mediumDate' }} – {{ p.endDate | date: 'mediumDate' }}</p>
          }
          @if (p.kind === 'Organiser') {
            <p>
              <mat-icon inline>admin_panel_settings</mat-icon>
              This link is for organisers. The event owner will need to approve you.
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
  `,
})
export class JoinPage implements OnInit {
  private readonly api = inject(JoinLinkApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly currentEventService = inject(CurrentEventService);
  private readonly token = inject(ActivatedRoute).snapshot.paramMap.get('token') ?? '';

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
