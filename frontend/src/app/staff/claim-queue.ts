import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ClaimStatus, ClaimSummary, Market, QueueAssignee } from '../core/api-models';
import { ClaimsApi } from '../core/claims-api';
import { MARKETS, MARKET_CURRENCY, STATUSES, STATUS_LABEL, ageInDays } from '../core/format';
import { problemMessage } from '../core/problem';
import { StatusBadge } from '../shared/status-badge';

/**
 * Officer work queue: open claims, oldest first (as returned by the API).
 * Tabs for "Unassigned" (to pick up), "Mine", and "All open"; optional status and market filters.
 */
@Component({
  selector: 'app-claim-queue',
  imports: [RouterLink, FormsModule, DatePipe, CurrencyPipe, StatusBadge],
  template: `
    <div class="row">
      <h1>Work queue</h1>
      <span class="spacer"></span>
      <button type="button" class="ghost" (click)="load()">Refresh</button>
    </div>

    <div class="row filters">
      <div class="tabs" role="tablist">
        @for (tab of tabs; track tab.value) {
          <button type="button" role="tab" [class.ghost]="assignee() !== tab.value" [attr.aria-selected]="assignee() === tab.value"
            (click)="assignee.set(tab.value); load()">{{ tab.label }}</button>
        }
      </div>
      <label class="inline">Status
        <select [ngModel]="status()" (ngModelChange)="status.set($event); load()">
          <option [ngValue]="null">All open</option>
          @for (s of statuses; track s) { <option [ngValue]="s">{{ statusLabel[s] }}</option> }
        </select>
      </label>
      <label class="inline">Market
        <select [ngModel]="market()" (ngModelChange)="market.set($event); load()">
          <option [ngValue]="null">All</option>
          @for (m of markets; track m) { <option [ngValue]="m">{{ m }}</option> }
        </select>
      </label>
    </div>

    @if (error()) {
      <p class="alert error">{{ error() }}</p>
    }

    <div class="card table-wrap">
      @if (loading()) {
        <p class="muted">Loading…</p>
      } @else {
        <table>
          <thead>
            <tr>
              <th>Claim</th><th>Claimant</th><th>Type</th><th>Status</th><th>Officer</th>
              <th class="num">Claimed</th><th class="num">Reserve</th><th class="num">Age</th><th>Received</th>
            </tr>
          </thead>
          <tbody>
            @for (c of claims(); track c.id) {
              <tr>
                <td><a [routerLink]="['/staff/claims', c.id]">{{ c.claimNumber }}</a></td>
                <td>{{ c.claimantName }}</td>
                <td>{{ c.type }} · {{ c.market }}</td>
                <td><app-status-badge [status]="c.status" /></td>
                <td>{{ c.assignedOfficerName ?? '—' }}</td>
                <td class="num">{{ c.claimedAmount | currency: currency[c.market] : 'code' }}</td>
                <td class="num">{{ c.reserveAmount === null ? '—' : (c.reserveAmount | currency: currency[c.market] : 'code') }}</td>
                <td class="num">{{ age(c.createdAt) }} d</td>
                <td>{{ c.createdAt | date: 'mediumDate' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="9" class="muted">Nothing here. Try another tab or filter.</td></tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: `
    .filters { margin-bottom: 1rem; }
    .tabs { display: flex; gap: 0.35rem; }
    .inline { flex-direction: row; align-items: center; gap: 0.5rem; }
  `,
})
export class ClaimQueue {
  private readonly api = inject(ClaimsApi);

  protected readonly tabs: { value: QueueAssignee; label: string }[] = [
    { value: 'Unassigned', label: 'Unassigned' },
    { value: 'Me', label: 'Mine' },
    { value: 'All', label: 'All open' },
  ];
  protected readonly statuses = STATUSES;
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly markets = MARKETS;
  protected readonly currency = MARKET_CURRENCY;
  protected readonly age = ageInDays;

  protected readonly assignee = signal<QueueAssignee>('Unassigned');
  protected readonly status = signal<ClaimStatus | null>(null);
  protected readonly market = signal<Market | null>(null);

  protected readonly claims = signal<ClaimSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getQueue({ assignee: this.assignee(), status: this.status(), market: this.market() }).subscribe({
      next: (claims) => {
        this.claims.set(claims);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(problemMessage(e));
        this.loading.set(false);
      },
    });
  }
}
