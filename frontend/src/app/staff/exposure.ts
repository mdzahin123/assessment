import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { LiabilityExposure } from '../core/api-models';
import { ClaimsApi } from '../core/claims-api';
import { problemMessage } from '../core/problem';

/**
 * Outstanding liability exposure, one card per market. Each card uses ONLY its own currency
 * (sent by the API) and there is deliberately no total across markets.
 */
@Component({
  selector: 'app-exposure',
  imports: [CurrencyPipe, DatePipe],
  template: `
    <div class="row">
      <h1>Liability exposure</h1>
      <span class="spacer"></span>
      <button type="button" class="ghost" (click)="load()">Refresh</button>
    </div>

    <p class="alert info">
      Exposure = the reserves (expected payouts) set by officers on <strong>open</strong> claims.
      Each market is shown in its own currency; markets are never added together.
      Claims <em>without a reserve</em> have not been assessed yet, so part of the exposure is not yet quantified.
    </p>

    @if (error()) {
      <p class="alert error">{{ error() }}</p>
    }

    @if (exposure(); as e) {
      @for (m of e.markets; track m.market) {
        <section class="card market">
          <h2>{{ m.market }} · {{ m.currency }}</h2>
          <div class="totals">
            <div><span class="muted">Open claims</span><strong>{{ m.openClaims }}</strong></div>
            <div><span class="muted">Reserved</span>
              <strong data-test="reserved">{{ m.reservedAmount | currency: m.currency : 'code' : '1.2-2' }}</strong></div>
            <div><span class="muted">No reserve yet</span>
              <strong data-test="without-reserve" [class.warn]="m.claimsWithoutReserve > 0">{{ m.claimsWithoutReserve }}</strong></div>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr><th>Type</th><th class="num">Open claims</th><th class="num">Reserved</th><th class="num">No reserve yet</th></tr>
              </thead>
              <tbody>
                @for (t of m.byType; track t.type) {
                  <tr>
                    <td>{{ t.type }}</td>
                    <td class="num">{{ t.openClaims }}</td>
                    <td class="num">{{ t.reservedAmount | currency: m.currency : 'code' : '1.2-2' }}</td>
                    <td class="num">{{ t.claimsWithoutReserve }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      } @empty {
        <p class="card muted">No open claims, so no outstanding exposure.</p>
      }
      <p class="muted note">Updated {{ e.generatedAt | date: 'mediumTime' }}.</p>
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .totals { display: flex; gap: 2rem; flex-wrap: wrap; margin-bottom: 0.75rem; }
    .totals div { display: flex; flex-direction: column; }
    .totals strong { font-size: 1.25rem; }
    .warn { color: var(--warn); }
    .note { font-size: 0.85rem; }
  `,
})
export class ExposurePage {
  private readonly api = inject(ClaimsApi);

  protected readonly exposure = signal<LiabilityExposure | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.error.set(null);
    this.api.getExposure().subscribe({
      next: (e) => this.exposure.set(e),
      error: (err) => this.error.set(problemMessage(err)),
    });
  }
}
