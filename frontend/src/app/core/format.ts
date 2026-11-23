import { ClaimAction, ClaimStatus, Market } from './api-models';

/** Each market implies its own currency, so amounts are always shown with their market's currency. */
export const MARKET_CURRENCY: Record<Market, string> = {
  SG: 'SGD',
  HK: 'HKD',
  AU: 'AUD',
  MY: 'MYR',
  TH: 'THB',
  ID: 'IDR',
};

export const STATUS_LABEL: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  InReview: 'In review',
  AwaitingInfo: 'Awaiting information',
  Settled: 'Settled',
  Rejected: 'Rejected',
};

export const ACTION_LABEL: Record<ClaimAction, string> = {
  Submitted: 'Claim submitted',
  Assigned: 'Picked up by officer',
  ReserveSet: 'Reserve set',
  InformationRequested: 'Information requested',
  InformationProvided: 'Information provided',
  Settled: 'Claim settled',
  Rejected: 'Claim rejected',
};

export const MARKETS: Market[] = ['SG', 'HK', 'AU', 'MY', 'TH', 'ID'];
export const STATUSES: ClaimStatus[] = ['Submitted', 'InReview', 'AwaitingInfo', 'Settled', 'Rejected'];

/** Whole days between an ISO timestamp and now: "how long has this been waiting?" */
export function ageInDays(isoTimestamp: string): number {
  return Math.floor((Date.now() - new Date(isoTimestamp).getTime()) / 86_400_000);
}

/** Today's date as yyyy-MM-dd in the user's local time (for the incident date picker's max). */
export function todayIso(): string {
  const now = new Date();
  const offsetMs = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offsetMs).toISOString().slice(0, 10);
}
