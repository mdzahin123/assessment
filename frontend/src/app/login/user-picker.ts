import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { User } from '../core/api-models';
import { ClaimsApi } from '../core/claims-api';
import { problemMessage } from '../core/problem';
import { Session } from '../core/session';

/** DEMO "login": pick a seeded user. Their ID is then sent as X-User-Id on every request. */
@Component({
  selector: 'app-user-picker',
  template: `
    <h1>Choose a demo user</h1>
    <p class="alert info">
      Demo only: there are no passwords. Picking a user simply sends their ID with every request.
      The server still enforces who can see and do what.
    </p>

    @if (error()) {
      <p class="alert error">{{ error() }}</p>
    }

    <div class="columns">
      <section class="card">
        <h2>Claimants</h2>
        @for (user of claimants(); track user.id) {
          <button type="button" class="user" (click)="choose(user)">
            <strong>{{ user.name }}</strong><span class="muted">{{ user.email }}</span>
          </button>
        }
      </section>
      <section class="card">
        <h2>Claims officers</h2>
        @for (user of officers(); track user.id) {
          <button type="button" class="user" (click)="choose(user)">
            <strong>{{ user.name }}</strong><span class="muted">{{ user.email }}</span>
          </button>
        }
      </section>
    </div>
  `,
  styles: `
    .columns { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 1rem; }
    .user { display: flex; flex-direction: column; align-items: flex-start; width: 100%; margin-bottom: 0.5rem;
      background: var(--surface); color: var(--text); border: 1px solid var(--border); }
    .user:hover { border-color: var(--accent); }
  `,
})
export class UserPicker {
  private readonly api = inject(ClaimsApi);
  private readonly session = inject(Session);
  private readonly router = inject(Router);

  protected readonly users = signal<User[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly claimants = computed(() => this.users().filter((u) => u.role === 'Claimant'));
  protected readonly officers = computed(() => this.users().filter((u) => u.role === 'Officer'));

  constructor() {
    this.api.getUsers().subscribe({
      next: (users) => this.users.set(users),
      error: (e) => this.error.set(problemMessage(e)),
    });
  }

  protected choose(user: User): void {
    this.session.login(user);
    void this.router.navigateByUrl(this.session.homeUrl());
  }
}
