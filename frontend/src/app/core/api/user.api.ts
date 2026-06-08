import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { UpdateUserRequest, User } from '../models/user';
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
}
