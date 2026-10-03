import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  Balances,
  Expense,
  ExpenseList,
  ExpensePayment,
  RecordPaymentRequest,
  UpsertExpenseRequest,
} from '../models/expense';
import { ENVIRONMENT } from '../../../environments/environment.model';

@Injectable({ providedIn: 'root' })
export class ExpenseApi {
  private readonly http = inject(HttpClient);
  private readonly env = inject(ENVIRONMENT);

  private getUrl(eventId: string, route = ''): string {
    return `${this.env.apiAddress}/api/v1/events/${eventId}/expenses${route}`;
  }

  list(eventId: string): Observable<ExpenseList> {
    return this.http.get<ExpenseList>(this.getUrl(eventId, '/'));
  }

  balances(eventId: string): Observable<Balances> {
    return this.http.get<Balances>(this.getUrl(eventId, '/balances'));
  }

  create(eventId: string, request: UpsertExpenseRequest): Observable<Expense> {
    return this.http.post<Expense>(this.getUrl(eventId, '/'), request);
  }

  update(eventId: string, id: number, request: UpsertExpenseRequest): Observable<Expense> {
    return this.http.put<Expense>(this.getUrl(eventId, `/${id}`), request);
  }

  delete(eventId: string, id: number): Observable<void> {
    return this.http.delete<void>(this.getUrl(eventId, `/${id}`));
  }

  recordPayment(eventId: string, request: RecordPaymentRequest): Observable<ExpensePayment> {
    return this.http.post<ExpensePayment>(this.getUrl(eventId, '/payments'), request);
  }

  deletePayment(eventId: string, id: number): Observable<void> {
    return this.http.delete<void>(this.getUrl(eventId, `/payments/${id}`));
  }
}
