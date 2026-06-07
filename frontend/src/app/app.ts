import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';

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
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly breakpointObserver = inject(BreakpointObserver);

  protected readonly title = signal('Amsterfam');

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
}
