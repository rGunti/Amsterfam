import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { CurrentEventService } from '../../core/event/current-event.service';
import { canPostNews, canViewNews } from '../../shared/event-status';

// Confirmed members only. Runs after eventGuard, so the event is loaded.
export const newsGuard: CanActivateFn = (route) => {
  const ev = inject(CurrentEventService).event();
  if (ev && canViewNews(ev)) {
    return true;
  }
  return inject(Router).createUrlTree(['/events', route.paramMap.get('id')]);
};

// Writing and editing posts: organisers of an event that isn't read-only.
export const newsComposeGuard: CanActivateFn = (route) => {
  const ev = inject(CurrentEventService).event();
  if (ev && canPostNews(ev)) {
    return true;
  }
  return inject(Router).createUrlTree(['/events', route.paramMap.get('id'), 'news']);
};
