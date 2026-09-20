import { Component, computed, inject, input } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

import {
  ExternalLinkDialog,
  ExternalLinkDialogData,
} from '../external-link-dialog/external-link-dialog';
import { linkify } from '../linkify';

/**
 * Plain user-written text (line breaks kept) with http(s) links made clickable.
 *
 * A plain click asks first, since the link was written by somebody else. Anything that opens
 * the link somewhere else (middle-click, Ctrl/Cmd/Shift-click, "Open in new tab" from the
 * context menu) is left to the browser: it's already a deliberate choice and goes to a new tab.
 */
@Component({
  selector: 'app-linkified-text',
  imports: [MatIconModule],
  // Text goes in spans: bare interpolation next to template whitespace would gain stray spaces,
  // and this component renders with `white-space: pre-wrap`.
  template: `@for (segment of segments(); track $index) {
    @if (segment.href; as href) {
      <a
        [href]="href"
        [attr.title]="href"
        target="_blank"
        rel="noopener noreferrer nofollow"
        (click)="onClick($event, href)"
        >{{ segment.text }}<mat-icon inline>open_in_new</mat-icon></a
      >
    } @else {
      <span>{{ segment.text }}</span>
    }
  }`,
  styles: `
    :host {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
    a {
      color: var(--mat-sys-primary);
      text-decoration: underline;
    }
    mat-icon {
      margin-left: 2px;
      vertical-align: -2px;
    }
  `,
})
export class LinkifiedText {
  private readonly dialog = inject(MatDialog);

  readonly text = input.required<string>();
  protected readonly segments = computed(() => linkify(this.text()));

  protected onClick(event: MouseEvent, href: string): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();
    this.dialog.open<ExternalLinkDialog, ExternalLinkDialogData>(ExternalLinkDialog, {
      data: { href },
    });
  }
}
