import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { CurrentUserService } from './core/api/current-user.service';
import { VersionApi } from './core/api/version.api';
import { EventApi } from './core/api/event.api';
import { CurrentEventService } from './core/event/current-event.service';

const authServiceMock: Partial<AuthService> = { logout: vi.fn() };
const currentUserServiceMock: Partial<CurrentUserService> = { user: signal(null) };
const versionApiMock: Partial<VersionApi> = {
  getBackendVersion: vi.fn(() => of({ version: '0.1.0', sha: 'test' })),
};
const eventApiMock: Partial<EventApi> = {
  getEvents: vi.fn(() => of([])),
};
const currentEventServiceMock: Partial<CurrentEventService> = {
  eventId: signal(null),
  event: signal(null),
};

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceMock },
        { provide: CurrentUserService, useValue: currentUserServiceMock },
        { provide: VersionApi, useValue: versionApiMock },
        { provide: EventApi, useValue: eventApiMock },
        { provide: CurrentEventService, useValue: currentEventServiceMock },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });
});
