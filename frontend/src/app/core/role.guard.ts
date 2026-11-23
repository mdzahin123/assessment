import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { UserRole } from './api-models';
import { Session } from './session';

/**
 * The "staff only" sign on a corridor: not logged in -> user picker; wrong role -> your own home screen.
 * This is for user experience only. The real lock is the API, which rejects the wrong role (403)
 * and hides other claimants' claims (404) no matter what the browser does.
 */
export function roleGuard(role: UserRole): CanActivateFn {
  return () => {
    const session = inject(Session);
    const router = inject(Router);
    const user = session.user();

    if (!user) return router.parseUrl('/login');
    return user.role === role ? true : router.parseUrl(session.homeUrl());
  };
}
