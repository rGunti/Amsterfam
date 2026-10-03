import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { CurrentEventService } from '../../core/event/current-event.service';
import { canViewExpenses } from '../../shared/event-status';

// Confirmed members only. Runs after eventGuard, so the event is loaded.
export const expensesGuard: CanActivateFn = (route) => {
  const ev = inject(CurrentEventService).event();
  if (ev && canViewExpenses(ev)) {
    return true;
  }
  return inject(Router).createUrlTree(['/events', route.paramMap.get('id')]);
};
