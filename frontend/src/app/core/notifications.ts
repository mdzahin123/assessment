import { Injectable, computed, inject, signal } from '@angular/core';
import { AppNotification } from './api-models';
import { ClaimsApi } from './claims-api';

const POLL_INTERVAL_MS = 10_000;

/**
 * The notification bell's data. The browser never talks to Kafka: notifications are created on the
 * server by the Kafka consumer, and the frontend simply asks for them every 10 seconds (polling).
 * Simple and good enough here; server push (SignalR/WebSockets) would be the next step.
 */
@Injectable({ providedIn: 'root' })
export class Notifications {
  private readonly api = inject(ClaimsApi);
  private timer: ReturnType<typeof setInterval> | undefined;

  readonly items = signal<AppNotification[]>([]);
  readonly unreadCount = computed(() => this.items().filter((n) => !n.isRead).length);

  start(): void {
    this.stop();
    this.refresh();
    this.timer = setInterval(() => this.refresh(), POLL_INTERVAL_MS);
  }

  stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = undefined;
    this.items.set([]);
  }

  refresh(): void {
    this.api.getNotifications().subscribe({
      next: (items) => this.items.set(items),
      error: () => {
        /* keep showing the last known list; the next poll will try again */
      },
    });
  }

  markRead(notification: AppNotification): void {
    if (notification.isRead) return;
    this.api.markNotificationRead(notification.id).subscribe(() =>
      this.items.update((items) => items.map((n) => (n.id === notification.id ? { ...n, isRead: true } : n))),
    );
  }
}
