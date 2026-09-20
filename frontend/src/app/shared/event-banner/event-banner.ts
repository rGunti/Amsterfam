import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';

/**
 * Event header image. The title (projected content) sits on the image's bottom edge over a
 * blurred, darkened strip, like a chat-app group banner. Fetches through HttpClient because
 * the API needs the bearer token, which a plain <img src> can't send.
 */
@Component({
  selector: 'app-event-banner',
  template: `
    <div class="banner">
      @if (objectUrl(); as url) {
        <img [src]="url" alt="" />
      }
      <div class="caption"><ng-content /></div>
    </div>
  `,
  styleUrl: './event-banner.scss',
})
export class EventBanner {
  private readonly http = inject(HttpClient);

  /** Absolute URL of the image endpoint. */
  readonly src = input.required<string>();
  /** Changes whenever the image does, so a replaced banner is re-fetched. */
  readonly version = input.required<string>();

  protected readonly objectUrl = signal<string | null>(null);

  constructor() {
    const revoke = () => {
      const url = this.objectUrl();
      if (url) {
        URL.revokeObjectURL(url);
      }
    };

    effect((onCleanup) => {
      this.version();
      const sub = this.http.get(this.src(), { responseType: 'blob' }).subscribe({
        next: (blob) => {
          revoke();
          this.objectUrl.set(URL.createObjectURL(blob));
        },
        error: () => {
          revoke();
          this.objectUrl.set(null);
        },
      });
      onCleanup(() => sub.unsubscribe());
    });

    inject(DestroyRef).onDestroy(revoke);
  }
}
