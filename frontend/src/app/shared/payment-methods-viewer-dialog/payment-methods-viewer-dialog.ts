import { Component, OnInit, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';

import { PaymentMethodApi } from '../../core/api/payment-method.api';
import { PaymentMethod } from '../../core/models/payment-method';

export interface PaymentMethodsViewerDialogData {
  userId: number;
  displayName: string;
}

@Component({
  selector: 'app-payment-methods-viewer-dialog',
  imports: [MatDialogModule, MatButtonModule, MatListModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>{{ data.displayName }}'s payment methods</h2>
    <mat-dialog-content>
      @if (loading()) {
        <p>Loading…</p>
      } @else if (methods().length === 0) {
        <p>No payment methods added yet.</p>
      } @else {
        <mat-list>
          @for (method of methods(); track method.id) {
            <mat-list-item [lines]="method.description ? 2 : 1">
              <mat-icon matListItemIcon>payments</mat-icon>
              <span matListItemTitle>
                @if (method.link) {
                  <a [href]="method.link" target="_blank" rel="noopener">{{ method.title }}</a>
                } @else {
                  {{ method.title }}
                }
              </span>
              @if (method.description) {
                <span matListItemLine>{{ method.description }}</span>
              }
            </mat-list-item>
          }
        </mat-list>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Close</button>
    </mat-dialog-actions>
  `,
})
export class PaymentMethodsViewerDialog implements OnInit {
  private readonly paymentMethodApi = inject(PaymentMethodApi);
  readonly data = inject<PaymentMethodsViewerDialogData>(MAT_DIALOG_DATA);

  readonly methods = signal<PaymentMethod[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.paymentMethodApi.getForUser(this.data.userId).subscribe({
      next: (methods) => {
        this.methods.set(methods);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
