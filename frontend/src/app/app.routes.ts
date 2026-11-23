import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { roleGuard } from './core/role.guard';
import { Session } from './core/session';

// The app's "building directory". Screens are lazy-loaded (downloaded the first time they are visited).
export const routes: Routes = [
  {
    path: 'login',
    title: 'ClaimFlow · Choose a demo user',
    loadComponent: () => import('./login/user-picker').then((m) => m.UserPicker),
  },
  {
    path: 'claims',
    canActivate: [roleGuard('Claimant')],
    children: [
      {
        path: '',
        title: 'ClaimFlow · My claims',
        loadComponent: () => import('./claimant/my-claims').then((m) => m.MyClaims),
      },
      {
        path: 'new',
        title: 'ClaimFlow · Report an incident',
        loadComponent: () => import('./claimant/report-claim').then((m) => m.ReportClaim),
      },
      {
        path: ':id',
        title: 'ClaimFlow · Claim',
        loadComponent: () => import('./claims/claim-detail').then((m) => m.ClaimDetailPage),
      },
    ],
  },
  {
    path: 'staff',
    canActivate: [roleGuard('Officer')],
    children: [
      {
        path: 'queue',
        title: 'ClaimFlow · Work queue',
        loadComponent: () => import('./staff/claim-queue').then((m) => m.ClaimQueue),
      },
      {
        path: 'claims/:id',
        title: 'ClaimFlow · Claim',
        loadComponent: () => import('./claims/claim-detail').then((m) => m.ClaimDetailPage),
      },
      { path: '', pathMatch: 'full', redirectTo: 'queue' },
    ],
  },
  // "/" (and unknown URLs) go to the current user's home screen, or the user picker.
  { path: '', pathMatch: 'full', redirectTo: () => inject(Session).homeUrl() },
  { path: '**', redirectTo: () => inject(Session).homeUrl() },
];
