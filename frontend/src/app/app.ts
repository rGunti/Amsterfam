import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { catchError, map, of } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './core/auth/auth.service';
import { CurrentUserService } from './core/api/current-user.service';
import { VersionApi } from './core/api/version.api';

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
    MatTooltipModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly authService = inject(AuthService);
  private readonly versionApi = inject(VersionApi);
  protected readonly currentUserService = inject(CurrentUserService);

  protected readonly title = signal('Amsterfam');

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

  protected readonly sidenavMode = computed(() => (this.isHandset().matches ? 'over' : 'side'));
  protected readonly navOpen = signal(true);

  constructor() {
    effect(() => this.navOpen.set(!this.isHandset().matches));
  }

  protected onNavLinkClick(): void {
    if (this.isHandset().matches) {
      this.navOpen.set(false);
    }
  }

  protected logout(): void {
    this.authService.logout();
  }
}
