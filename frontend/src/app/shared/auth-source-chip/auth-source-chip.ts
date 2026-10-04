import { Component, computed, input } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';

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

/**
 * Where a handle comes from, shown next to it: handles are only unique per sign-in source,
 * so "@klaus" on Discord and "@klaus" with an internal account are different people.
 */
@Component({
  selector: 'app-auth-source-chip',
  imports: [MatTooltipModule],
  template: `
    @if (label(); as label) {
      <span class="chip" [matTooltip]="'Signs in with ' + label">{{ label }}</span>
    }
  `,
  styles: `
    .chip {
      display: inline-block;
      margin-left: 6px;
      padding: 0 8px;
      border-radius: 999px;
      background: var(--mat-sys-surface-container-high);
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-label-small);
      line-height: 20px;
      vertical-align: middle;
    }
  `,
})
export class AuthSourceChip {
  readonly source = input<string | null>(null);

  readonly label = computed(() => {
    const source = this.source();
    return source ? authSourceLabel(source) : null;
  });
}
