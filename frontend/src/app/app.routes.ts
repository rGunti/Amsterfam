import { Routes } from '@angular/router';

import { authGuard } from './core/auth/auth.guard';
import { eventGuard } from './core/event/event.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/events/events-list').then((m) => m.EventsList),
    canActivate: [authGuard],
  },
  {
    path: 'events/new',
    loadComponent: () => import('./features/events/events-create').then((m) => m.EventsCreate),
    canActivate: [authGuard],
  },
  {
    path: 'events/:id',
    canActivate: [authGuard, eventGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./features/events/events-detail').then((m) => m.EventsDetail),
      },
    ],
  },
  {
    path: 'profile',
    loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
    canActivate: [authGuard],
  },
  {
    path: 'auth/callback',
    loadComponent: () => import('./features/auth/auth-callback').then((m) => m.AuthCallback),
  },
  {
    path: 'offline',
    loadComponent: () => import('./features/offline/offline').then((m) => m.Offline),
  },
];
