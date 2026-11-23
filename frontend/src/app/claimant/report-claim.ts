import { CurrencyPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Policy } from '../core/api-models';
import { ClaimsApi } from '../core/claims-api';
import { MARKET_CURRENCY, todayIso } from '../core/format';
import { problemMessage } from '../core/problem';

const DESCRIPTION_MAX = 2000; // same limit as the API (Claim.DescriptionMaxLength)

/**
 * Report an incident. The form checks the obvious things instantly (required, length, positive amount)
 * for fast feedback; the API re-checks everything (incident date within the policy period, max 2 decimals...)
 * and its message is shown if it says no.
 */
@Component({
  selector: 'app-report-claim',
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe],
  template: `
    <h1>Report an incident</h1>

    @if (error()) {
      <p class="alert error" role="alert">{{ error() }}</p>
    }

    <form class="card form" [formGroup]="form" (ngSubmit)="submit()">
      <label>
        Policy
        <select formControlName="policyId">
          <option value="" disabled>Choose the policy this incident falls under</option>
          @for (p of policies(); track p.id) {
            <option [value]="p.id">{{ p.policyNumber }} · {{ p.type }} · {{ p.market }}</option>
          }
        </select>
        @if (selectedPolicy(); as p) {
          <span class="muted hint">
            Covered {{ p.validFrom }} to {{ p.validTo }}, limit {{ p.coverageLimit | currency: currency[p.market] : 'code' : '1.0-0' }}
          </span>
        }
        @if (showError('policyId')) { <span class="field-error">Please choose a policy.</span> }
      </label>

      <label>
        Date of the incident
        <input type="date" formControlName="incidentDate" [max]="today" />
        @if (showError('incidentDate')) { <span class="field-error">Please enter the date it happened.</span> }
      </label>

      <label>
        What happened?
        <textarea formControlName="description" [attr.maxlength]="descriptionMax"
          placeholder="Where and how it happened, and what was damaged."></textarea>
        <span class="muted hint">{{ descriptionLength() }} / {{ descriptionMax }}</span>
        @if (showError('description')) { <span class="field-error">Please describe what happened.</span> }
      </label>

      <label>
        Amount you are claiming
        <input type="number" formControlName="claimedAmount" min="0.01" step="0.01" inputmode="decimal" />
        @if (showError('claimedAmount')) { <span class="field-error">Enter an amount greater than zero.</span> }
      </label>

      <div class="row">
        <button type="submit" [disabled]="submitting()">{{ submitting() ? 'Submitting…' : 'Submit claim' }}</button>
        <a class="button ghost" routerLink="/claims">Cancel</a>
      </div>
    </form>
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: 1rem; max-width: 640px; }
    .hint { font-weight: 400; font-size: 0.85rem; }
  `,
})
export class ReportClaim {
  private readonly api = inject(ClaimsApi);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly currency = MARKET_CURRENCY;
  protected readonly today = todayIso();
  protected readonly descriptionMax = DESCRIPTION_MAX;

  protected readonly policies = signal<Policy[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    policyId: ['', Validators.required],
    incidentDate: ['', Validators.required],
    description: ['', [Validators.required, Validators.maxLength(DESCRIPTION_MAX)]],
    claimedAmount: [null as number | null, [Validators.required, Validators.min(0.01)]],
  });

  // Form values as signals, so the hint texts update as the user types.
  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly selectedPolicy = computed(() => this.policies().find((p) => p.id === this.formValue().policyId));
  protected readonly descriptionLength = computed(() => this.formValue().description?.length ?? 0);

  constructor() {
    this.api.getMyPolicies().subscribe({
      next: (policies) => this.policies.set(policies),
      error: (e) => this.error.set(problemMessage(e)),
    });
  }

  protected showError(field: keyof typeof this.form.controls): boolean {
    const control = this.form.controls[field];
    return control.invalid && (control.touched || control.dirty);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched(); // reveal all field messages
      return;
    }
    const value = this.form.getRawValue();
    this.submitting.set(true);
    this.error.set(null);
    this.api
      .submitClaim({
        policyId: value.policyId,
        incidentDate: value.incidentDate,
        description: value.description.trim(),
        claimedAmount: value.claimedAmount!,
      })
      .subscribe({
        next: (claim) => void this.router.navigate(['/claims', claim.id]),
        error: (e) => {
          this.error.set(problemMessage(e));
          this.submitting.set(false);
        },
      });
  }
}
