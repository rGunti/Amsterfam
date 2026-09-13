import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ENVIRONMENT } from '../../../environments/environment.model';

export interface BackendVersion {
  version: string;
  sha: string;
}

@Injectable({ providedIn: 'root' })
export class VersionApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  getBackendVersion(): Observable<BackendVersion> {
    return this.http.get<BackendVersion>(`${this.env.apiAddress}/api/v1/version`);
  }
}
