import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TeamWorkload } from '../core/api-models';
import { ClaimsApi } from '../core/claims-api';
import { problemMessage } from '../core/problem';
import { Session } from '../core/session';

/** Team workload: who is carrying what, and recent decisions. All numbers are computed by the API. */
@Component({
  selector: 'app-team-workload',
  imports: [RouterLink, DatePipe, DecimalPipe],
  template: `
    <div class="row">
      <h1>Team workload</h1>
      <span class="spacer"></span>
      <button type="button" class="ghost" (click)="load()">Refresh</button>
    </div>

    @if (error()) {
      <p class="alert error">{{ error() }}</p>
    }

    @if (workload(); as w) {
      <section class="card unassigned">
        <div>
          <div class="big">{{ w.unassignedClaims }}</div>
          <div class="muted">unassigned claims waiting to be picked up</div>
        </div>
        <a class="button ghost" routerLink="/staff/queue">Open work queue</a>
      </section>

      <div class="card table-wrap">
        <table>
          <thead>
            <tr>
              <th>Officer</th>
              <th class="num">Open claims</th>
              <th class="num">Average age (days)</th>
              <th class="num">Settled (last {{ w.decisionWindowDays }} days)</th>
              <th class="num">Rejected (last {{ w.decisionWindowDays }} days)</th>
            </tr>
          </thead>
          <tbody>
            @for (o of w.officers; track o.officerId) {
              <tr [class.me]="o.officerId === myId()">
                <td>{{ o.officerName }} @if (o.officerId === myId()) { <span class="muted">(you)</span> }</td>
                <td class="num">{{ o.openClaims }}</td>
                <td class="num">{{ o.averageOpenAgeDays === null ? '—' : (o.averageOpenAgeDays | number: '1.1-1') }}</td>
                <td class="num">{{ o.settledLast7Days }}</td>
                <td class="num">{{ o.rejectedLast7Days }}</td>
              </tr>
            }
          </tbody>
        </table>
        <p class="muted note">
          Open = in review or awaiting information. Age is measured from when the claim was submitted.
          Updated {{ w.generatedAt | date: 'mediumTime' }}.
        </p>
      </div>
    } @else if (!error()) {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .unassigned { display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; }
    .big { font-size: 2rem; font-weight: 700; line-height: 1; }
    tr.me td { font-weight: 600; }
    .note { margin: 0.75rem 0 0; font-size: 0.85rem; }
  `,
})
export class TeamWorkloadPage {
  private readonly api = inject(ClaimsApi);
  private readonly session = inject(Session);

  protected readonly workload = signal<TeamWorkload | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly myId = () => this.session.user()?.id;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.error.set(null);
    this.api.getWorkload().subscribe({
      next: (w) => this.workload.set(w),
      error: (e) => this.error.set(problemMessage(e)),
    });
  }
}
