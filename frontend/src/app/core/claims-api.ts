import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AppNotification,
  ClaimDetail,
  ClaimSummary,
  LiabilityExposure,
  Policy,
  QueueFilter,
  SubmitClaimRequest,
  TeamWorkload,
  User,
} from './api-models';

/**
 * The only place in the frontend that knows the backend URLs.
 * Relative "/api/..." URLs: in development the Angular dev server forwards them to the
 * .NET API on localhost:5006 (proxy.conf.json), so the browser never needs CORS.
 * The X-User-Id header is added automatically by userIdInterceptor.
 */
@Injectable({ providedIn: 'root' })
export class ClaimsApi {
  private readonly http = inject(HttpClient);

  // --- users / me
  getUsers(): Observable<User[]> {
    return this.http.get<User[]>('/api/users');
  }

  getMyPolicies(): Observable<Policy[]> {
    return this.http.get<Policy[]>('/api/me/policies');
  }

  getMyClaims(): Observable<ClaimSummary[]> {
    return this.http.get<ClaimSummary[]>('/api/me/claims');
  }

  getNotifications(): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>('/api/me/notifications');
  }

  markNotificationRead(id: string): Observable<void> {
    return this.http.post<void>(`/api/me/notifications/${id}/read`, null);
  }

  // --- claims
  submitClaim(request: SubmitClaimRequest): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>('/api/claims', request);
  }

  getClaim(id: string): Observable<ClaimDetail> {
    return this.http.get<ClaimDetail>(`/api/claims/${id}`);
  }

  getQueue(filter: QueueFilter): Observable<ClaimSummary[]> {
    let params = new HttpParams().set('assignee', filter.assignee);
    if (filter.status) params = params.set('status', filter.status);
    if (filter.market) params = params.set('market', filter.market);
    return this.http.get<ClaimSummary[]>('/api/claims', { params });
  }

  assign(id: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/assign`, null);
  }

  setReserve(id: string, amount: number): Observable<ClaimDetail> {
    return this.http.put<ClaimDetail>(`/api/claims/${id}/reserve`, { amount });
  }

  requestInformation(id: string, question: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/information-requests`, { question });
  }

  provideInformation(id: string, requestId: string, response: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/information-requests/${requestId}/response`, {
      response,
    });
  }

  settle(id: string, amount: number): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/settle`, { amount });
  }

  reject(id: string, reason: string): Observable<ClaimDetail> {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/reject`, { reason });
  }

  // --- dashboards (officers only; computed live by the API)
  getWorkload(): Observable<TeamWorkload> {
    return this.http.get<TeamWorkload>('/api/dashboard/workload');
  }

  getExposure(): Observable<LiabilityExposure> {
    return this.http.get<LiabilityExposure>('/api/dashboard/exposure');
  }
}
