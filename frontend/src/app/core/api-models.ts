// TypeScript shapes of the API's request/response DTOs.
// Source of truth: the backend's OpenAPI contract (http://localhost:5006/openapi/v1.json).
// Hand-written for speed; generating them from the contract would be the next step.
// Enums are string unions because the API sends enums as strings ("InReview").

export type UserRole = 'Claimant' | 'Officer';
export type ClaimType = 'Motor' | 'Property';
export type Market = 'SG' | 'HK' | 'AU' | 'MY' | 'TH' | 'ID';
export type ClaimStatus = 'Submitted' | 'InReview' | 'AwaitingInfo' | 'Settled' | 'Rejected';
export type ClaimAction =
  | 'Submitted'
  | 'Assigned'
  | 'ReserveSet'
  | 'InformationRequested'
  | 'InformationProvided'
  | 'Settled'
  | 'Rejected';
export type QueueAssignee = 'All' | 'Me' | 'Unassigned';

export interface User {
  id: string;
  name: string;
  email: string;
  role: UserRole;
}

export interface Policy {
  id: string;
  policyNumber: string;
  type: ClaimType;
  market: Market;
  coverageLimit: number;
  validFrom: string; // yyyy-MM-dd
  validTo: string;
}

export interface ClaimSummary {
  id: string;
  claimNumber: string;
  type: ClaimType;
  market: Market;
  status: ClaimStatus;
  incidentDate: string;
  claimedAmount: number;
  reserveAmount: number | null; // always null for claimants
  claimantName: string;
  assignedOfficerId: string | null;
  assignedOfficerName: string | null;
  createdAt: string; // ISO timestamp, UTC
  updatedAt: string;
}

export interface ClaimHistoryItem {
  action: ClaimAction;
  fromStatus: ClaimStatus | null;
  toStatus: ClaimStatus;
  actorName: string;
  note: string | null;
  occurredAt: string;
}

export interface InformationRequest {
  id: string;
  question: string;
  response: string | null;
  isOpen: boolean;
  createdAt: string;
  answeredAt: string | null;
}

export interface ClaimDetail {
  id: string;
  claimNumber: string;
  policyNumber: string;
  type: ClaimType;
  market: Market;
  status: ClaimStatus;
  incidentDate: string;
  description: string;
  claimedAmount: number;
  reserveAmount: number | null;
  settledAmount: number | null;
  rejectionReason: string | null;
  claimantId: string;
  claimantName: string;
  assignedOfficerId: string | null;
  assignedOfficerName: string | null;
  createdAt: string;
  updatedAt: string;
  history: ClaimHistoryItem[];
  informationRequests: InformationRequest[];
}

export interface AppNotification {
  id: string;
  claimId: string;
  claimNumber: string;
  message: string;
  createdAt: string;
  isRead: boolean;
}

export interface SubmitClaimRequest {
  policyId: string;
  incidentDate: string;
  description: string;
  claimedAmount: number;
}

export interface QueueFilter {
  status?: ClaimStatus | null;
  market?: Market | null;
  assignee: QueueAssignee;
}
