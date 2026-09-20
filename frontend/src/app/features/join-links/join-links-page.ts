import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { JoinLinkApi } from '../../core/api/join-link.api';
import { UserApi } from '../../core/api/user.api';
import { CurrentEventService } from '../../core/event/current-event.service';
import { JoinLinkKind, JoinLinkResponse } from '../../core/models/join-link';
import { PendingAttendeesCard } from './pending-attendees-card';
import { copyJoinLink } from '../../shared/join-link';
import { localIsoDate } from '../../shared/local-date';

@Component({
  selector: 'app-join-links-page',
  imports: [
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatTooltipModule,
    PendingAttendeesCard,
  ],
  templateUrl: './join-links-page.html',
  styleUrl: './join-links-page.scss',
})
export class JoinLinksPage implements OnInit {
  private readonly api = inject(JoinLinkApi);
  private readonly userApi = inject(UserApi);
  private readonly snackBar = inject(MatSnackBar);
  private readonly currentEventService = inject(CurrentEventService);

  readonly event = this.currentEventService.event;
  readonly links = signal<JoinLinkResponse[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly isOwner = signal(false);
  readonly today = localIsoDate();

  label = '';
  expires = '';
  maxUses: number | null = null;

  ngOnInit(): void {
    this.userApi.getMe().subscribe((me) => this.isOwner.set(this.event()?.createdById === me.id));
    this.load();
  }

  get isDraft(): boolean {
    return this.event()?.status === 'Draft';
  }

  private load(): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.api.list(ev.id).subscribe({
      next: (links) => {
        this.links.set(links);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  create(kind: JoinLinkKind): void {
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.busy.set(true);
    this.api
      .create(ev.id, {
        kind,
        label: this.label.trim() || null,
        // Expire at the end of the chosen local day.
        expiresAt: this.expires ? new Date(`${this.expires}T23:59:59`).toISOString() : null,
        maxUses: this.maxUses || null,
      })
      .subscribe({
        next: (link) => {
          this.busy.set(false);
          this.label = '';
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
    const ev = this.event();
    if (!ev) {
      return;
    }
    this.busy.set(true);
    this.api.revoke(ev.id, link.id).subscribe({
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
    copyJoinLink(this.snackBar, link.token);
  }

  kindIcon(kind: JoinLinkKind): string {
    return kind === 'Organiser' ? 'admin_panel_settings' : 'person_add';
  }

  kindName(kind: JoinLinkKind): string {
    return kind === 'Organiser' ? 'Organiser link' : 'Attendee link';
  }
}
