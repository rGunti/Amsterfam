import { CanDeactivateFn } from '@angular/router';
import { Observable } from 'rxjs';

/** A page whose work would be lost on navigating away. */
export interface HasUnsavedChanges {
  /**
   * True to let the user leave; otherwise ask them (e.g. with a confirm dialog) and emit
   * their answer. Lives on the page so the dialog code stays in the page's lazy chunk.
   */
  canLeave(): boolean | Observable<boolean>;
}

/**
 * Asks before leaving a page with unsaved changes. Reloads and closing the tab aren't
 * navigations, so the page also needs a `beforeunload` listener.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) =>
  component.canLeave();
