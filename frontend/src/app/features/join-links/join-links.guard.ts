import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { CurrentEventService } from '../../core/event/current-event.service';
import { canManageJoinLinks } from '../../shared/event-status';

// Organisers only, and not once the event is read-only. Runs after eventGuard, so the
// event is loaded.
export const joinLinksGuard: CanActivateFn = (route) => {
  const ev = inject(CurrentEventService).event();
  if (ev && canManageJoinLinks(ev)) {
    return true;
  }
  return inject(Router).createUrlTree(['/events', route.paramMap.get('id')]);
};
