import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

export interface OrganiserAvatar {
  userId: number;
  displayName: string;
  avatarUrl: string | null;
}

const DEFAULT_MAX_VISIBLE_ORGANISERS = 6;

@Component({
  selector: 'app-organiser-avatar-stack',
  imports: [MatIconModule, MatTooltipModule],
  templateUrl: './organiser-avatar-stack.html',
  styleUrl: './organiser-avatar-stack.scss',
})
export class OrganiserAvatarStack {
  readonly organisers = input<readonly OrganiserAvatar[]>([]);
  readonly ownerId = input<number | null>(null);
  readonly maxVisible = input(DEFAULT_MAX_VISIBLE_ORGANISERS);

  readonly sorted = computed(() => {
    const ownerId = this.ownerId();
    return this.organisers()
      .slice()
      .sort(
        (a, b) =>
          (a.userId === ownerId ? 0 : 1) - (b.userId === ownerId ? 0 : 1) ||
          a.displayName.localeCompare(b.displayName),
      );
  });

  readonly visible = computed(() => {
    const all = this.sorted();
    const max = this.maxVisible();
    if (all.length <= max) {
      return { avatars: all, overflowCount: 0 };
    }
    return {
      avatars: all.slice(0, max - 1),
      overflowCount: all.length - (max - 1),
    };
  });

  tooltipFor(organiser: OrganiserAvatar): string {
    return organiser.displayName + (organiser.userId === this.ownerId() ? ' · Event owner' : '');
  }
}
