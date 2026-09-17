import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarRef, TextOnlySnackBar } from '@angular/material/snack-bar';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { catchError, map, of } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './core/auth/auth.service';
import { CurrentUserService } from './core/api/current-user.service';
import { EventApi } from './core/api/event.api';
import { CurrentEventService } from './core/event/current-event.service';
import { EventResponse } from './core/models/event';
import { VersionApi } from './core/api/version.api';
import { InstallPromptService } from './core/install-prompt/install-prompt.service';
import { statusIcon } from './shared/event-status-icon';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatDividerModule,
    MatMenuModule,
    MatTooltipModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly authService = inject(AuthService);
  private readonly versionApi = inject(VersionApi);
  private readonly eventApi = inject(EventApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly installPromptService = inject(InstallPromptService);
  protected readonly currentUserService = inject(CurrentUserService);
  protected readonly currentEventService = inject(CurrentEventService);
  protected readonly canInstall = this.installPromptService.canInstall;

  protected readonly title = signal('Amsterfam');

  private readonly _myEvents = signal<EventResponse[]>([]);
  protected readonly otherEvents = computed(() => {
    const currentId = this.currentEventService.eventId();
    return [...this._myEvents()]
      .filter((ev) => ev.id !== currentId)
      .sort((a, b) => a.name.localeCompare(b.name));
  });
  protected readonly inEventContext = computed(() => this.currentEventService.eventId() !== null);
  protected readonly statusIcon = statusIcon;

  protected readonly frontendVersion = `${environment.version}+${environment.sha}`;
  protected readonly backendVersion = toSignal(
    this.versionApi.getBackendVersion().pipe(
      map((v) => `${v.version}+${v.sha}`),
      catchError(() => of(null)),
    ),
    { initialValue: null },
  );

  private readonly isHandset = toSignal(this.breakpointObserver.observe(Breakpoints.Handset), {
    initialValue: { matches: false, breakpoints: {} },
  });

  // The mobile side nav has its own event switcher, so the toolbar one only
  // needs to show once there's room for it alongside the title.
  protected readonly showToolbarEventSwitcher = computed(() => !this.isHandset().matches);

  protected readonly sidenavMode = computed(() => (this.isHandset().matches ? 'over' : 'side'));
  protected readonly navOpen = signal(true);

  private offlineSnackBarRef: MatSnackBarRef<TextOnlySnackBar> | null = null;

  constructor() {
    effect(() => this.navOpen.set(!this.isHandset().matches));

    effect(() => {
      // Also depend on eventId so a newly created/joined event (navigated to
      // straight after creation) shows up in the switcher without a reload.
      this.currentEventService.eventId();

      if (this.authService.isAuthenticated()) {
        this.eventApi.getEvents().subscribe({
          next: (events) => this._myEvents.set(events),
          error: () => {
            // Keep whatever we last had rather than clearing it on a transient error.
          },
        });
      } else {
        this._myEvents.set([]);
      }
    });

    const showOfflineNotice = () => {
      if (this.offlineSnackBarRef) {
        return;
      }
      this.offlineSnackBarRef = this.snackBar.open("You're offline");
    };
    const dismissOfflineNotice = () => {
      this.offlineSnackBarRef?.dismiss();
      this.offlineSnackBarRef = null;
    };

    if (!navigator.onLine) {
      showOfflineNotice();
    }
    window.addEventListener('offline', showOfflineNotice);
    window.addEventListener('online', dismissOfflineNotice);
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('offline', showOfflineNotice);
      window.removeEventListener('online', dismissOfflineNotice);
      dismissOfflineNotice();
    });
  }

  protected onNavLinkClick(): void {
    if (this.isHandset().matches) {
      this.navOpen.set(false);
    }
  }

  protected switchEvent(id: string): void {
    this.router.navigate(['/events', id]);
  }

  protected async promptInstall(): Promise<void> {
    const outcome = await this.installPromptService.promptInstall();
    if (outcome === 'accepted') {
      this.snackBar.open('Amsterfam installed', undefined, { duration: 3000 });
    } else if (outcome === 'dismissed') {
      this.snackBar.open('Install dismissed', undefined, { duration: 3000 });
    }
  }
}
