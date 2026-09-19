import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { CurrentEventService } from '../../core/event/current-event.service';

// Find a date is only relevant while the event is a draft, and only to confirmed members.
// Runs after eventGuard, so the event is already loaded.
export const datePollGuard: CanActivateFn = (route) => {
  const ev = inject(CurrentEventService).event();
  const isMember = ev?.currentUserRole === 'Organiser' || ev?.currentUserRole === 'Attendee';
  if (ev?.status === 'Draft' && isMember) {
    return true;
  }
  return inject(Router).createUrlTree(['/events', route.paramMap.get('id')]);
};
