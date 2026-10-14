import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  DietaryOption,
  UpdateAboutRequest,
  UpdateUserRequest,
  User,
  UserProfile,
} from '../models/user';
import { NotificationSettings } from '../models/notification-settings';
import { ENVIRONMENT } from '../../../environments/environment.model';

@Injectable({ providedIn: 'root' })
export class UserApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private getUrl(route: string): string {
    return `${this.env.apiAddress}${route}`;
  }

  getMe(): Observable<User> {
    return this.http.get<User>(this.getUrl('/api/v1/me'));
  }

  updateMe(request: UpdateUserRequest): Observable<User> {
    return this.http.put<User>(this.getUrl('/api/v1/me'), request);
  }

  updateAbout(request: UpdateAboutRequest): Observable<User> {
    return this.http.put<User>(this.getUrl('/api/v1/me/about'), request);
  }

  getNotificationSettings(): Observable<NotificationSettings> {
    return this.http.get<NotificationSettings>(this.getUrl('/api/v1/me/notification-settings'));
  }

  updateNotificationSettings(emailNotifications: boolean): Observable<NotificationSettings> {
    return this.http.put<NotificationSettings>(this.getUrl('/api/v1/me/notification-settings'), {
      emailNotifications,
    });
  }

  /** Saves a custom address as pending and mails it a confirmation link. */
  setNotificationEmail(email: string): Observable<NotificationSettings> {
    return this.http.put<NotificationSettings>(this.getUrl('/api/v1/me/notification-email'), {
      email,
    });
  }

  resendNotificationEmail(): Observable<NotificationSettings> {
    return this.http.post<NotificationSettings>(
      this.getUrl('/api/v1/me/notification-email/resend'),
      null,
    );
  }

  confirmNotificationEmail(token: string): Observable<NotificationSettings> {
    return this.http.post<NotificationSettings>(
      this.getUrl('/api/v1/me/notification-email/confirm'),
      { token },
    );
  }

  /** Drops the custom address, so news goes to the login address again. */
  clearNotificationEmail(): Observable<NotificationSettings> {
    return this.http.delete<NotificationSettings>(this.getUrl('/api/v1/me/notification-email'));
  }

  getProfile(profileHandle: string): Observable<UserProfile> {
    return this.http.get<UserProfile>(
      this.getUrl(`/api/v1/users/${encodeURIComponent(profileHandle)}/profile`),
    );
  }

  getDietaryOptions(): Observable<DietaryOption[]> {
    return this.http.get<DietaryOption[]>(this.getUrl('/api/v1/dietary-options'));
  }
}
