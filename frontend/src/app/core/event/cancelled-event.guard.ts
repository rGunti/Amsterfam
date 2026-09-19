import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { EventApi } from '../api/event.api';

// The cancelled dead end is only for events that really are cancelled and hidden from
// this user; anyone else is sent to the event itself (eventGuard handles unknown ids).
export const cancelledEventGuard: CanActivateFn = (route) => {
  const router = inject(Router);
  const id = route.paramMap.get('id');
  if (!id) {
    return of(router.createUrlTree(['/']));
  }

  return inject(EventApi)
    .getEvent(id)
    .pipe(
      map((event) =>
        event.status === 'Cancelled' && event.currentUserRole !== 'Organiser'
          ? true
          : router.createUrlTree(['/events', id]),
      ),
      catchError(() => of(router.createUrlTree(['/']))),
    );
};
