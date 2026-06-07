import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';

import { UserApi } from '../../core/api/user.api';
import { User } from '../../core/models/user';

@Component({
  selector: 'app-main',
  imports: [RouterLink, MatCardModule, MatButtonModule],
  templateUrl: './main.html',
  styleUrl: './main.scss',
})
export class Main implements OnInit {
  readonly user = signal<User | null>(null);

  constructor(private readonly userApi: UserApi) {}

  ngOnInit(): void {
    this.userApi.getMe().subscribe((user) => this.user.set(user));
  }
}
