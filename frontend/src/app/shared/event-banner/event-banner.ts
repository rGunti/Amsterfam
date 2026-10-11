import { Component, input } from '@angular/core';

import { AuthImage } from '../auth-image/auth-image';

/**
 * Event header image. The title (projected content) sits on the image's bottom edge over a
 * blurred, darkened strip, like a chat-app group banner.
 */
@Component({
  selector: 'app-event-banner',
  imports: [AuthImage],
  template: `
    <div class="banner">
      <app-auth-image class="banner-image" [src]="src()" [version]="version()" />
      <div class="caption"><ng-content /></div>
    </div>
  `,
  styleUrl: './event-banner.scss',
})
export class EventBanner {
  /** Absolute URL of the image endpoint. */
  readonly src = input.required<string>();
  /** Changes whenever the image does, so a replaced banner is re-fetched. */
  readonly version = input.required<string>();
}
