import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ClaimDetail } from '../core/api-models';
import { ClaimsApi } from '../core/claims-api';
import { ACTION_LABEL, MARKET_CURRENCY } from '../core/format';
import { problemMessage } from '../core/problem';
import { Session } from '../core/session';
import { StatusBadge } from '../shared/status-badge';

/**
 * One claim: facts, questions & answers, timeline, and the actions the current user may take.
 * Shared by claimants (/claims/:id) and officers (/staff/claims/:id).
 * The buttons shown mirror the server's rules for convenience; the server still enforces them all.
 */
@Component({
  selector: 'app-claim-detail',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, CurrencyPipe, StatusBadge],
  template: `
    <a [routerLink]="backLink()">← Back</a>

    @if (loadError()) {
      <p class="alert error">{{ loadError() }}</p>
    }

    @if (claim(); as c) {
      <div class="row heading">
        <h1>Claim {{ c.claimNumber }}</h1>
        <app-status-badge [status]="c.status" />
      </div>

      @if (actionError()) {
        <p class="alert error" role="alert">{{ actionError() }}</p>
      }

      <section class="card">
        <dl class="facts">
          <div><dt>Policy</dt><dd>{{ c.policyNumber }} · {{ c.type }} · {{ c.market }}</dd></div>
          <div><dt>Claimant</dt><dd>{{ c.claimantName }}</dd></div>
          <div><dt>Incident date</dt><dd>{{ c.incidentDate | date: 'mediumDate' }}</dd></div>
          <div><dt>Claimed</dt><dd>{{ c.claimedAmount | currency: currencyOf(c) : 'code' }}</dd></div>
          @if (isOfficer()) {
            <div><dt>Reserve (internal)</dt>
              <dd>{{ c.reserveAmount === null ? 'Not set' : (c.reserveAmount | currency: currencyOf(c) : 'code') }}</dd></div>
          }
          <div><dt>Handled by</dt><dd>{{ c.assignedOfficerName ?? 'Not yet assigned' }}</dd></div>
          @if (c.settledAmount !== null) {
            <div><dt>Settled amount</dt><dd class="ok">{{ c.settledAmount | currency: currencyOf(c) : 'code' }}</dd></div>
          }
          @if (c.rejectionReason) {
            <div class="wide"><dt>Reason for rejection</dt><dd>{{ c.rejectionReason }}</dd></div>
          }
          <div class="wide"><dt>What happened</dt><dd class="pre">{{ c.description }}</dd></div>
        </dl>
      </section>

      <!-- Claimant: answer the officer's open question -->
      @if (!isOfficer() && openRequest(); as req) {
        <section class="card highlight">
          <h2>We need more information</h2>
          <p class="pre">{{ req.question }}</p>
          <label>
            Your answer
            <textarea [formControl]="responseCtrl"></textarea>
          </label>
          <div class="row actions">
            <button type="button" [disabled]="busy() || responseCtrl.invalid" (click)="respond(req.id)">Send answer</button>
          </div>
        </section>
      }

      <!-- Officer actions -->
      @if (isOfficer()) {
        <section class="card">
          <h2>Actions</h2>
          @if (c.status === 'Submitted') {
            <p class="muted">This claim is waiting to be picked up.</p>
            <button type="button" [disabled]="busy()" (click)="pickUp()">Pick up this claim</button>
          } @else if (c.status === 'Settled' || c.status === 'Rejected') {
            <p class="muted">This claim is closed. No further actions.</p>
          } @else if (!isMine()) {
            <p class="muted">Assigned to {{ c.assignedOfficerName }}. Only they can act on it.</p>
          } @else {
            <div class="action-grid">
              <div class="action">
                <label>Reserve (expected payout)
                  <input type="number" min="0.01" step="0.01" [formControl]="reserveCtrl" />
                </label>
                <button type="button" class="ghost" [disabled]="busy() || reserveCtrl.invalid" (click)="setReserve()">Save reserve</button>
              </div>

              @if (c.status === 'InReview') {
                <div class="action">
                  <label>Ask the claimant
                    <textarea [formControl]="questionCtrl" placeholder="e.g. Please send photos of the damage."></textarea>
                  </label>
                  <button type="button" class="ghost" [disabled]="busy() || questionCtrl.invalid" (click)="requestInfo()">Request information</button>
                </div>

                <div class="action">
                  <label>Settle for
                    <input type="number" min="0.01" step="0.01" [formControl]="settleCtrl" />
                  </label>
                  <button type="button" [disabled]="busy() || settleCtrl.invalid" (click)="settle()">Settle claim</button>
                </div>
              } @else {
                <p class="muted">Waiting for the claimant to answer your question before it can be settled.</p>
              }

              <div class="action">
                <label>Reason for rejection
                  <textarea [formControl]="rejectCtrl"></textarea>
                </label>
                <button type="button" class="danger" [disabled]="busy() || rejectCtrl.invalid" (click)="reject()">Reject claim</button>
              </div>
            </div>
          }
        </section>
      }

      @if (c.informationRequests.length > 0) {
        <section class="card">
          <h2>Questions and answers</h2>
          @for (r of c.informationRequests; track r.id) {
            <div class="qa">
              <p><strong>Q:</strong> {{ r.question }} <small class="muted">({{ r.createdAt | date: 'medium' }})</small></p>
              @if (r.response) {
                <p><strong>A:</strong> {{ r.response }} <small class="muted">({{ r.answeredAt | date: 'medium' }})</small></p>
              } @else {
                <p class="muted">Waiting for the claimant's answer.</p>
              }
            </div>
          }
        </section>
      }

      <section class="card">
        <h2>Timeline</h2>
        <ol class="timeline">
          @for (h of c.history; track $index) {
            <li>
              <strong>{{ actionLabel[h.action] }}</strong>
              <span class="muted"> by {{ h.actorName }} · {{ h.occurredAt | date: 'medium' }}</span>
              @if (h.note) { <div class="pre">{{ h.note }}</div> }
            </li>
          }
        </ol>
      </section>
    } @else if (!loadError()) {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .heading { margin-top: 0.75rem; }
    .heading h1 { margin: 0; }
    .facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 0.75rem 1.5rem; margin: 0; }
    .facts dt { color: var(--muted); font-size: 0.85rem; }
    .facts dd { margin: 0; font-weight: 600; }
    .facts .wide { grid-column: 1 / -1; }
    .facts dd.pre, .pre { white-space: pre-wrap; font-weight: 400; }
    .ok { color: var(--ok); }
    .highlight { border-color: var(--warn); background: var(--warn-soft); }
    .actions { margin-top: 0.75rem; }
    .action-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 1rem; }
    .action { display: flex; flex-direction: column; gap: 0.5rem; align-items: flex-start; }
    .action label { width: 100%; }
    .qa { border-left: 3px solid var(--border); padding-left: 0.75rem; margin-bottom: 0.75rem; }
    .qa p { margin: 0.2rem 0; }
    .timeline { margin: 0; padding-left: 1.25rem; }
    .timeline li { margin-bottom: 0.6rem; }
  `,
})
export class ClaimDetailPage {
  /** From the route ":id" (withComponentInputBinding). */
  readonly id = input.required<string>();

  private readonly api = inject(ClaimsApi);
  private readonly session = inject(Session);

  protected readonly actionLabel = ACTION_LABEL;
  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly isOfficer = this.session.isOfficer;
  protected readonly isMine = computed(() => this.claim()?.assignedOfficerId === this.session.user()?.id);
  protected readonly openRequest = computed(() => this.claim()?.informationRequests.find((r) => r.isOpen) ?? null);
  protected readonly backLink = computed(() => (this.isOfficer() ? '/staff/queue' : '/claims'));

  protected readonly reserveCtrl = new FormControl<number | null>(null, [Validators.required, Validators.min(0.01)]);
  protected readonly settleCtrl = new FormControl<number | null>(null, [Validators.required, Validators.min(0.01)]);
  protected readonly questionCtrl = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1000)] });
  protected readonly rejectCtrl = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1000)] });
  protected readonly responseCtrl = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1000)] });

  constructor() {
    // (Re)load whenever the route's :id changes. untracked: only "id" should re-trigger loading.
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  protected currencyOf(c: ClaimDetail): string {
    return MARKET_CURRENCY[c.market];
  }

  protected pickUp(): void {
    this.run(this.api.assign(this.id()));
  }

  protected setReserve(): void {
    this.run(this.api.setReserve(this.id(), this.reserveCtrl.value!));
  }

  protected requestInfo(): void {
    this.run(this.api.requestInformation(this.id(), this.questionCtrl.value.trim()), () => this.questionCtrl.reset());
  }

  protected respond(requestId: string): void {
    this.run(this.api.provideInformation(this.id(), requestId, this.responseCtrl.value.trim()), () => this.responseCtrl.reset());
  }

  protected settle(): void {
    this.run(this.api.settle(this.id(), this.settleCtrl.value!));
  }

  protected reject(): void {
    this.run(this.api.reject(this.id(), this.rejectCtrl.value.trim()));
  }

  private load(id: string): void {
    this.loadError.set(null);
    this.api.getClaim(id).subscribe({
      next: (claim) => this.show(claim),
      error: (e) => this.loadError.set(problemMessage(e)),
    });
  }

  private show(claim: ClaimDetail): void {
    this.claim.set(claim);
    this.reserveCtrl.setValue(claim.reserveAmount);
    if (this.settleCtrl.value === null && claim.reserveAmount !== null) this.settleCtrl.setValue(claim.reserveAmount);
  }

  /** Every action: call the API, show the updated claim it returns, or show why it was refused. */
  private run(action: Observable<ClaimDetail>, onSuccess?: () => void): void {
    this.busy.set(true);
    this.actionError.set(null);
    action.subscribe({
      next: (claim) => {
        this.show(claim);
        this.busy.set(false);
        onSuccess?.();
      },
      error: (e) => {
        this.actionError.set(problemMessage(e));
        this.busy.set(false);
        // 409 = the claim changed under us (e.g. a colleague picked it up first): show its real state.
        if (e instanceof HttpErrorResponse && e.status === 409) this.load(this.id());
      },
    });
  }
}
