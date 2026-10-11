import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';

/**
 * An image from the API. Fetches through HttpClient because the API needs the bearer token,
 * which a plain <img src> can't send, and shows the result as an object URL.
 *
 * Size, `max-height` and `object-fit` are set on the host by the parent; the image fills the
 * host and inherits the other two. Shows nothing until loaded, or if loading fails.
 */
@Component({
  selector: 'app-auth-image',
  template: `
    @if (objectUrl(); as url) {
      <img [src]="url" [alt]="alt()" />
    }
  `,
  styles: `
    :host {
      display: block;
    }

    img {
      display: block;
      width: 100%;
      height: 100%;
      max-height: inherit;
      object-fit: inherit;
    }
  `,
})
export class AuthImage {
  private readonly http = inject(HttpClient);

  /** Absolute URL of the image endpoint. */
  readonly src = input.required<string>();
  /** Changes whenever the image does, so a replaced image is re-fetched. */
  readonly version = input.required<string>();
  readonly alt = input('');

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
