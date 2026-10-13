import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleChange, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog/confirm-dialog';
import { MAX_DEVICE_NAME_LENGTH, PushDevice } from '../../core/models/push';
import { PushService, PushStatus } from '../../core/notifications/push.service';

const REASONS: Record<Exclude<PushStatus, 'ready'>, string> = {
  loading: 'Checking whether push notifications are available…',
  install:
    'On iPhone and iPad, add Amsterfam to your Home Screen and open it from there (iOS 16.4 or later).',
  unsupported: "This browser can't receive push notifications.",
  off: "Push notifications aren't set up for Amsterfam yet.",
  error: "Couldn't check whether push notifications are available. Try again later.",
  blocked: 'Notifications are blocked for Amsterfam. Allow them in your browser settings.',
};

/** Push notifications on the profile page: this device's switch and all the member's devices. */
@Component({
  selector: 'app-push-settings-card',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSlideToggleModule,
    MatTooltipModule,
  ],
  templateUrl: './push-settings-card.html',
  styleUrl: './push-settings-card.scss',
})
export class PushSettingsCard {
  readonly push = inject(PushService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly maxNameLength = MAX_DEVICE_NAME_LENGTH;
  readonly busy = signal(false);

  /** Why the switch can't be used here, or null when it can. */
  readonly reason = computed(() => {
    const status = this.push.status();
    return status === 'ready' ? null : REASONS[status];
  });
  readonly editingId = signal<number | null>(null);
  readonly nameControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(MAX_DEVICE_NAME_LENGTH)],
  });

  async toggle(change: MatSlideToggleChange): Promise<void> {
    this.busy.set(true);
    try {
      if (change.checked) {
        await this.push.enable();
      } else {
        await this.push.disable();
      }
    } catch {
      change.source.checked = this.push.subscribed();
      // A refused permission prompt shows up as the "blocked" reason on the card instead.
      if (this.push.status() !== 'blocked') {
        this.snackBar.open('Could not change push notifications', 'Dismiss', { duration: 5000 });
      }
    } finally {
      this.busy.set(false);
    }
  }

  isThisDevice(device: PushDevice): boolean {
    return device.endpoint === this.push.currentEndpoint();
  }

  startRename(device: PushDevice): void {
    this.nameControl.setValue(device.name);
    this.editingId.set(device.id);
  }

  cancelRename(): void {
    this.editingId.set(null);
  }

  async saveRename(device: PushDevice): Promise<void> {
    const name = this.nameControl.value.trim();
    if (!name || this.nameControl.invalid) {
      return;
    }
    try {
      await this.push.rename(device.id, name);
      this.editingId.set(null);
    } catch {
      this.snackBar.open('Could not rename the device', 'Dismiss', { duration: 3000 });
    }
  }

  revoke(device: PushDevice): void {
    const data: ConfirmDialogData = {
      title: 'Remove device',
      message: this.isThisDevice(device)
        ? 'Stop push notifications on this device?'
        : `Stop push notifications on “${device.name}”?`,
      confirmLabel: 'Remove',
    };
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe(async (ok) => {
        if (!ok) {
          return;
        }
        try {
          await this.push.revoke(device);
        } catch {
          this.snackBar.open('Could not remove the device', 'Dismiss', { duration: 3000 });
        }
      });
  }
}
