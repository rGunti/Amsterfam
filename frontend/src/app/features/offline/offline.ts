import { Component } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-offline',
  imports: [MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './offline.html',
  styleUrl: './offline.scss',
})
export class Offline {
  reload(): void {
    window.location.reload();
  }
}
