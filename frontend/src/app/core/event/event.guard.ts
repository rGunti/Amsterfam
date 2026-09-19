import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { map, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { CurrentEventService } from './current-event.service';

export const eventGuard: CanActivateFn = (route) => {
  const currentEventService = inject(CurrentEventService);
  const router = inject(Router);
  const snackBar = inject(MatSnackBar);

  const id = route.paramMap.get('id');
  if (!id) {
    return of(router.createUrlTree(['/']));
  }

  return currentEventService.loadEvent(id).pipe(
    map((event) => {
      // Cancelled events are only visible to organisers; everyone else gets a dead end.
      if (event.status === 'Cancelled' && event.currentUserRole !== 'Organiser') {
        currentEventService.clear();
        return router.createUrlTree(['/events', id, 'cancelled']);
      }
      return true;
    }),
    catchError(() => {
      snackBar.open('Event not found', 'Dismiss', { duration: 3000 });
      return of(router.createUrlTree(['/']));
    }),
  );
};
