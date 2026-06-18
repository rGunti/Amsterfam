import { Routes } from '@angular/router';

import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/events/events-list').then((m) => m.EventsList),
    canActivate: [authGuard],
  },
  {
    path: 'events/:id',
    loadComponent: () => import('./features/events/events-detail').then((m) => m.EventsDetail),
    canActivate: [authGuard],
  },
  {
    path: 'profile',
    loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
    canActivate: [authGuard],
  },
  {
    path: 'auth/callback',
    redirectTo: '',
  },
];
