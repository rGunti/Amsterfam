import { Component, ViewEncapsulation, computed, inject, input } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';

import {
  ExternalLinkDialog,
  ExternalLinkDialogData,
} from '../external-link-dialog/external-link-dialog';
import { isPlainLeftClick } from '../external-link';
import { renderMarkdown } from './render-markdown';

/**
 * Renders user-written Markdown (see {@link renderMarkdown} for what's allowed). Like
 * `<app-linkified-text>`, a plain click on a link asks before leaving the app.
 */
@Component({
  selector: 'app-markdown',
  template: '',
  host: {
    class: 'app-markdown',
    '[innerHTML]': 'html()',
    '(click)': 'onClick($event)',
  },
  // The markup comes from innerHTML, so the styles can't be scoped to the template.
  encapsulation: ViewEncapsulation.None,
  styleUrl: './markdown.scss',
})
export class Markdown {
  private readonly dialog = inject(MatDialog);

  readonly text = input.required<string>();
  protected readonly html = computed(() => renderMarkdown(this.text()));

  protected onClick(event: MouseEvent): void {
    const link = (event.target as Element | null)?.closest('a[href]');
    if (!link || !isPlainLeftClick(event)) {
      return;
    }
    event.preventDefault();
    this.dialog.open<ExternalLinkDialog, ExternalLinkDialogData>(ExternalLinkDialog, {
      data: { href: link.getAttribute('href')! },
    });
  }
}
