import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { UpdateUserRequest, User } from '../models/user';

@Injectable({ providedIn: 'root' })
export class UserApi {
  constructor(private readonly http: HttpClient) {}

  getMe(): Observable<User> {
    return this.http.get<User>('/api/v1/me');
  }

  updateMe(request: UpdateUserRequest): Observable<User> {
    return this.http.put<User>('/api/v1/me', request);
  }
}
