import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { CurrentEventService } from '../../core/event/current-event.service';
import { canViewTimeline } from '../../shared/event-status';

// Confirmed members only. Runs after eventGuard, so the event is loaded.
export const timelineGuard: CanActivateFn = (route) => {
  const ev = inject(CurrentEventService).event();
  if (ev && canViewTimeline(ev)) {
    return true;
  }
  return inject(Router).createUrlTree(['/events', route.paramMap.get('id')]);
};
