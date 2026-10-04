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

  getProfile(userId: number): Observable<UserProfile> {
    return this.http.get<UserProfile>(this.getUrl(`/api/v1/users/${userId}/profile`));
  }

  getDietaryOptions(): Observable<DietaryOption[]> {
    return this.http.get<DietaryOption[]>(this.getUrl('/api/v1/dietary-options'));
  }
}
