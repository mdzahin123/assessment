import { Injectable, computed, signal } from '@angular/core';
import { User } from './api-models';

const STORAGE_KEY = 'claimflow.demoUser';

/**
 * Who is "logged in" (DEMO ONLY: the user picks themselves from a list; nothing is secret).
 * Remembered in localStorage so a page refresh keeps you logged in. Storage can be unavailable
 * (private mode, blocked site data), so every access is wrapped in try/catch and the app still works.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  readonly user = signal<User | null>(readStoredUser());
  readonly isOfficer = computed(() => this.user()?.role === 'Officer');

  login(user: User): void {
    this.user.set(user);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    } catch {
      /* storage unavailable: still logged in for this tab */
    }
  }

  logout(): void {
    this.user.set(null);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* ignore */
    }
  }

  /** Where each kind of user starts. */
  homeUrl(): string {
    const user = this.user();
    if (!user) return '/login';
    return user.role === 'Officer' ? '/staff/queue' : '/claims';
  }
}

function readStoredUser(): User | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as User) : null;
  } catch {
    return null;
  }
}
