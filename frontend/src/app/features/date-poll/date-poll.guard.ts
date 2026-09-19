import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { CurrentEventService } from '../../core/event/current-event.service';
import { canUseDatePoll } from '../../shared/event-status';

// Find a date is only relevant while the event is looking for a date (organisers can also
// prepare the range while it's a draft). Runs after eventGuard, so the event is loaded.
export const datePollGuard: CanActivateFn = (route) => {
  const ev = inject(CurrentEventService).event();
  if (ev && canUseDatePoll(ev)) {
    return true;
  }
  return inject(Router).createUrlTree(['/events', route.paramMap.get('id')]);
};
