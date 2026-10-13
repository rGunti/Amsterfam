import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

import { DiscordLogo } from '../discord-logo/discord-logo';

const KNOWN_SOURCES: Record<string, string> = {
  discord: 'Discord',
  internal: 'Internal',
};

/** Human label for an Authentik source slug, e.g. "discord" → "Discord". */
export function authSourceLabel(slug: string): string {
  return (
    KNOWN_SOURCES[slug] ??
    slug
      .split(/[-_]/)
      .filter(Boolean)
      .map((word) => word[0].toUpperCase() + word.slice(1))
      .join(' ')
  );
}

/** Material icon per source; Discord gets its own mark, anything unknown a generic one. */
const SOURCE_ICONS: Record<string, string> = {
  internal: 'badge',
};
const FALLBACK_ICON = 'login';

/**
 * Where a handle comes from, shown as an icon next to it: handles are only unique per
 * sign-in source, so "@klaus" on Discord and "@klaus" with an internal account are
 * different people.
 */
@Component({
  selector: 'app-auth-source-icon',
  imports: [MatIconModule, MatTooltipModule, DiscordLogo],
  template: `
    @if (source(); as source) {
      <!-- Focusable so keyboard users get the tooltip too. -->
      <span
        class="source"
        role="img"
        tabindex="0"
        [attr.aria-label]="label()"
        [matTooltip]="label()"
      >
        @if (source === 'discord') {
          <app-discord-logo class="discord" />
        } @else {
          <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
        }
      </span>
    }
  `,
  styles: `
    .source {
      display: inline-flex;
      margin-left: 6px;
      vertical-align: middle;
      color: var(--mat-sys-on-surface-variant);
    }

    mat-icon,
    .discord {
      width: 18px;
      height: 18px;
      font-size: 18px;
    }
  `,
})
export class AuthSourceIcon {
  readonly source = input<string | null>(null);

  readonly label = computed(() => {
    const source = this.source();
    return source ? authSourceLabel(source) : null;
  });

  readonly icon = computed(() => SOURCE_ICONS[this.source() ?? ''] ?? FALLBACK_ICON);
}
