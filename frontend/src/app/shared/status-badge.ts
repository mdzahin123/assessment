import { Component, computed, input } from '@angular/core';
import { ClaimStatus } from '../core/api-models';
import { STATUS_LABEL } from '../core/format';

/** A coloured pill showing a claim's status. */
@Component({
  selector: 'app-status-badge',
  template: `<span class="badge" [class]="status()">{{ label() }}</span>`,
  styles: `
    .badge { display: inline-block; padding: 0.1rem 0.6rem; border-radius: 999px; font-size: 0.8rem; font-weight: 600; }
    .Submitted { background: var(--accent-soft); color: var(--accent); }
    .InReview { background: var(--warn-soft); color: var(--warn); }
    .AwaitingInfo { background: var(--warn-soft); color: var(--warn); border: 1px dashed var(--warn); }
    .Settled { background: var(--ok-soft); color: var(--ok); }
    .Rejected { background: var(--danger-soft); color: var(--danger); }
  `,
})
export class StatusBadge {
  readonly status = input.required<ClaimStatus>();
  protected readonly label = computed(() => STATUS_LABEL[this.status()]);
}
