import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AppNotification } from './core/api-models';
import { Notifications } from './core/notifications';
import { Session } from './core/session';

/** The app shell: header (who am I, navigation, notification bell) + the frame where screens appear. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe],
  template: `
    <header class="topbar">
      <a class="brand" routerLink="/">ClaimFlow</a>

      @if (session.user(); as user) {
        <nav>
          @if (session.isOfficer()) {
            <a routerLink="/staff/queue" routerLinkActive="active">Work queue</a>
          } @else {
            <a routerLink="/claims" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">My claims</a>
            <a routerLink="/claims/new" routerLinkActive="active">Report an incident</a>
          }
        </nav>

        <div class="right">
          <div class="bell">
            <button type="button" class="ghost" (click)="toggleBell()" [attr.aria-expanded]="bellOpen()">
              Notifications
              @if (notifications.unreadCount() > 0) {
                <span class="count">{{ notifications.unreadCount() }}</span>
              }
            </button>
            @if (bellOpen()) {
              <div class="panel" role="dialog" aria-label="Notifications">
                @for (n of notifications.items(); track n.id) {
                  <button type="button" class="note" [class.unread]="!n.isRead" (click)="openNotification(n)">
                    <span>{{ n.message }}</span>
                    <small>{{ n.createdAt | date: 'medium' }}</small>
                  </button>
                } @empty {
                  <p class="muted pad">No notifications yet.</p>
                }
              </div>
            }
          </div>
          <span class="who">{{ user.name }} <small>({{ user.role }})</small></span>
          <button type="button" class="ghost" (click)="switchUser()">Switch user</button>
        </div>
      }
    </header>

    <main>
      <router-outlet />
    </main>
  `,
  styles: `
    .topbar { display: flex; align-items: center; gap: 1.5rem; padding: 0.75rem 1.5rem; background: var(--surface);
      border-bottom: 1px solid var(--border); flex-wrap: wrap; }
    .brand { font-weight: 700; font-size: 1.15rem; color: var(--text); text-decoration: none; }
    nav { display: flex; gap: 1rem; }
    nav a { color: var(--muted); text-decoration: none; padding: 0.25rem 0; }
    nav a.active { color: var(--accent); border-bottom: 2px solid var(--accent); }
    .right { margin-left: auto; display: flex; align-items: center; gap: 0.75rem; }
    .who small { color: var(--muted); }
    .bell { position: relative; }
    .count { background: var(--danger); color: #fff; border-radius: 999px; padding: 0 0.45rem; font-size: 0.75rem; margin-left: 0.3rem; }
    .panel { position: absolute; right: 0; top: 2.4rem; width: min(380px, 90vw); max-height: 420px; overflow: auto; z-index: 10;
      background: var(--surface); border: 1px solid var(--border); border-radius: 8px; box-shadow: 0 8px 24px rgb(0 0 0 / 0.12); }
    .note { display: flex; flex-direction: column; gap: 0.2rem; width: 100%; text-align: left; padding: 0.65rem 0.85rem;
      background: none; border: 0; border-bottom: 1px solid var(--border); border-radius: 0; color: var(--text); cursor: pointer; }
    .note.unread { background: var(--accent-soft); font-weight: 600; }
    .note small { color: var(--muted); font-weight: 400; }
    .pad { padding: 0.85rem; margin: 0; }
    main { padding: 1.5rem; max-width: 1100px; margin: 0 auto; }
  `,
})
export class App {
  protected readonly session = inject(Session);
  protected readonly notifications = inject(Notifications);
  private readonly router = inject(Router);
  protected readonly bellOpen = signal(false);

  constructor() {
    // Start polling notifications while someone is logged in; stop (and clear) when they switch user.
    effect(() => {
      if (this.session.user()) this.notifications.start();
      else this.notifications.stop();
    });
  }

  /** Opening the bell fetches the latest right away instead of waiting for the next 10-second poll. */
  protected toggleBell(): void {
    const opening = !this.bellOpen();
    this.bellOpen.set(opening);
    if (opening) this.notifications.refresh();
  }

  protected openNotification(n: AppNotification): void {
    this.notifications.markRead(n);
    this.bellOpen.set(false);
    const base = this.session.isOfficer() ? '/staff/claims' : '/claims';
    void this.router.navigate([base, n.claimId]);
  }

  protected switchUser(): void {
    this.bellOpen.set(false);
    this.session.logout();
    void this.router.navigateByUrl('/login');
  }
}
