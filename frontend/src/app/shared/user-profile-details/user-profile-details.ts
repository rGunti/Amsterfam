import { Component, computed, input } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';

import { ProfileDetails } from '../../core/models/user';
import { formatBirthday } from '../birthday';
import { LinkifiedText } from '../linkified-text/linkified-text';

/** Read-only "about me" fields. Fields left empty are skipped. */
@Component({
  selector: 'app-user-profile-details',
  imports: [MatChipsModule, MatIconModule, LinkifiedText],
  template: `
    @let p = profile();
    @if (isEmpty()) {
      <p class="empty">{{ emptyText() }}</p>
    } @else {
      <dl>
        @if (p.pronouns) {
          <div class="row">
            <dt><mat-icon>badge</mat-icon> Pronouns</dt>
            <dd>{{ p.pronouns }}</dd>
          </div>
        }
        @if (p.location) {
          <div class="row">
            <dt><mat-icon>location_on</mat-icon> Location</dt>
            <dd>{{ p.location }}</dd>
          </div>
        }
        @if (birthday(); as b) {
          <div class="row">
            <dt><mat-icon>cake</mat-icon> Birthday</dt>
            <dd>{{ b }}</dd>
          </div>
        }
        @if (p.dietaryOptions.length > 0 || p.dietaryNotes) {
          <div class="row">
            <dt><mat-icon>restaurant</mat-icon> Dietary needs</dt>
            <dd>
              @if (p.dietaryOptions.length > 0) {
                <mat-chip-set aria-label="Dietary needs">
                  @for (option of p.dietaryOptions; track option.id) {
                    <mat-chip>{{ option.label }}</mat-chip>
                  }
                </mat-chip-set>
              }
              @if (p.dietaryNotes) {
                <app-linkified-text class="notes" [text]="p.dietaryNotes" />
              }
            </dd>
          </div>
        }
        @if (p.bio) {
          <div class="row">
            <dt><mat-icon>info</mat-icon> About</dt>
            <dd><app-linkified-text [text]="p.bio" /></dd>
          </div>
        }
      </dl>
    }
  `,
  styles: `
    dl {
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    dt {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      color: var(--mat-sys-on-surface-variant);

      mat-icon {
        height: 16px;
        width: 16px;
        font-size: 16px;
      }
    }

    dd {
      margin: 2px 0 0 22px;
      min-width: 0;
    }

    .notes {
      display: block;
      margin-top: 4px;
    }

    .empty {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class UserProfileDetails {
  readonly profile = input.required<ProfileDetails>();
  readonly emptyText = input('Nothing shared yet.');

  readonly birthday = computed(() => {
    const b = this.profile().birthday;
    return b ? formatBirthday(b) : null;
  });

  readonly isEmpty = computed(() => {
    const p = this.profile();
    return (
      !p.pronouns &&
      !p.location &&
      !p.bio &&
      !p.birthday &&
      !p.dietaryNotes &&
      p.dietaryOptions.length === 0
    );
  });
}
