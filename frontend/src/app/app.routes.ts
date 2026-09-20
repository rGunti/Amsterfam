import { Routes } from '@angular/router';

import { authGuard } from './core/auth/auth.guard';
import { eventGuard } from './core/event/event.guard';
import { cancelledEventGuard } from './core/event/cancelled-event.guard';
import { datePollGuard } from './features/date-poll/date-poll.guard';
import type { StatusPageData } from './shared/status-page/status-page';

const loadStatusPage = () => import('./shared/status-page/status-page').then((m) => m.StatusPage);

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
    path: 'join/:token',
    loadComponent: () => import('./features/join/join-page').then((m) => m.JoinPage),
    canActivate: [authGuard],
  },
  {
    // Declared before 'events/:id' so it's matched without going through eventGuard.
    path: 'events/:id/cancelled',
    loadComponent: loadStatusPage,
    canActivate: [authGuard, cancelledEventGuard],
    data: {
      icon: 'event_busy',
      title: 'This event was cancelled',
      message: 'The organisers have cancelled this event, so its details are no longer available.',
    } satisfies StatusPageData,
  },
  {
    path: 'events/:id',
    canActivate: [authGuard, eventGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./features/events/events-detail').then((m) => m.EventsDetail),
      },
      {
        path: 'find-a-date',
        loadComponent: () =>
          import('./features/date-poll/date-poll-page').then((m) => m.DatePollPage),
        canActivate: [datePollGuard],
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
  {
    path: '**',
    loadComponent: loadStatusPage,
    data: {
      icon: 'explore_off',
      title: 'Page not found',
      message: "There's nothing here. The link may be broken or the page may have moved.",
    } satisfies StatusPageData,
  },
];
