import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const AUTH_ENDPOINTS = ['/auth/login', '/auth/register', '/auth/refresh'];

/** 附加 Bearer token；401 時嘗試一次 refresh 後重送原請求。 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);

  const isAuthEndpoint = AUTH_ENDPOINTS.some((path) => request.url.includes(path));
  const token = auth.accessToken();

  const authorised =
    token && !isAuthEndpoint
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;

  return next(authorised).pipe(
    catchError((error: unknown) => {
      const isUnauthorised = error instanceof HttpErrorResponse && error.status === 401;

      if (!isUnauthorised || isAuthEndpoint || !auth.refreshToken()) {
        return throwError(() => error);
      }

      return from(auth.refresh()).pipe(
        switchMap(() =>
          next(request.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken()}` } })),
        ),
        catchError((refreshError: unknown) => {
          auth.logout();
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};
