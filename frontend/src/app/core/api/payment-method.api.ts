import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PaymentMethod, UpsertPaymentMethodRequest } from '../models/payment-method';
import { ENVIRONMENT } from '../../../environments/environment.model';

@Injectable({ providedIn: 'root' })
export class PaymentMethodApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private getUrl(route: string): string {
    return `${this.env.apiAddress}${route}`;
  }

  getMine(): Observable<PaymentMethod[]> {
    return this.http.get<PaymentMethod[]>(this.getUrl('/api/v1/me/payment-methods'));
  }

  getForUser(userId: number): Observable<PaymentMethod[]> {
    return this.http.get<PaymentMethod[]>(this.getUrl(`/api/v1/users/${userId}/payment-methods`));
  }

  create(request: UpsertPaymentMethodRequest): Observable<PaymentMethod> {
    return this.http.post<PaymentMethod>(this.getUrl('/api/v1/me/payment-methods'), request);
  }

  update(id: number, request: UpsertPaymentMethodRequest): Observable<PaymentMethod> {
    return this.http.put<PaymentMethod>(this.getUrl(`/api/v1/me/payment-methods/${id}`), request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(this.getUrl(`/api/v1/me/payment-methods/${id}`));
  }
}
