import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Session } from './session';

/**
 * The "mailroom": stamps every API request with the demo user's ID, so no screen has to remember to.
 * If the API answers 401 (e.g. the database was reset and this user no longer exists),
 * forget the stored user and go back to the user picker.
 */
export const userIdInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(Session);
  const router = inject(Router);

  const user = session.user();
  const request = user ? req.clone({ setHeaders: { 'X-User-Id': user.id } }) : req;

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        session.logout();
        void router.navigateByUrl('/login');
      }
      return throwError(() => error);
    }),
  );
};
