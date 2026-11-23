import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ClaimSummary } from '../core/api-models';
import { ClaimsApi } from '../core/claims-api';
import { MARKET_CURRENCY } from '../core/format';
import { problemMessage } from '../core/problem';
import { StatusBadge } from '../shared/status-badge';

/** Claimant home: "my claims" with their current status. */
@Component({
  selector: 'app-my-claims',
  imports: [RouterLink, DatePipe, CurrencyPipe, StatusBadge],
  template: `
    <div class="row">
      <h1>My claims</h1>
      <span class="spacer"></span>
      <button type="button" class="ghost" (click)="load()">Refresh</button>
      <a class="button" routerLink="/claims/new">Report an incident</a>
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
              <th>Claim</th><th>Type</th><th>Status</th><th>Incident date</th>
              <th class="num">Claimed</th><th>Last update</th>
            </tr>
          </thead>
          <tbody>
            @for (c of claims(); track c.id) {
              <tr>
                <td><a [routerLink]="['/claims', c.id]">{{ c.claimNumber }}</a></td>
                <td>{{ c.type }} · {{ c.market }}</td>
                <td><app-status-badge [status]="c.status" /></td>
                <td>{{ c.incidentDate | date: 'mediumDate' }}</td>
                <td class="num">{{ c.claimedAmount | currency: currency[c.market] : 'code' }}</td>
                <td>{{ c.updatedAt | date: 'medium' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="6" class="muted">You have no claims yet.</td></tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
})
export class MyClaims {
  private readonly api = inject(ClaimsApi);
  protected readonly currency = MARKET_CURRENCY;

  protected readonly claims = signal<ClaimSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getMyClaims().subscribe({
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
