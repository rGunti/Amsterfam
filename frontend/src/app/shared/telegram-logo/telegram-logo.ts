import { Component } from '@angular/core';

/**
 * Telegram's mark: a paper plane in a circle. Decorative: it takes the text colour and is sized
 * by the host (1em by default), so put a label next to it or on a wrapper.
 */
@Component({
  selector: 'app-telegram-logo',
  template: `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="12" cy="12" r="12" fill="currentColor" />
      <path
        fill="#fff"
        d="M5.4 11.7 16.9 7.3c.5-.2 1 .1.8.9l-2 9.3c-.1.6-.5.8-1 .5l-3-2.2-1.5 1.4c-.2.2-.3.3-.6.3l.2-3.1 5.6-5.1c.2-.2 0-.3-.4-.1l-6.9 4.4-3-.9c-.6-.2-.6-.6.2-1z"
      />
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      width: 1em;
      height: 1em;
    }

    svg {
      width: 100%;
      height: 100%;
    }
  `,
})
export class TelegramLogo {}
