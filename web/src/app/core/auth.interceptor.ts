import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { NotificationService } from './notification.service';

const AUTH_ENDPOINTS = ['/auth/login', '/auth/register', '/auth/refresh'];

/** 附加 Bearer token；401 時嘗試一次 refresh 後重送原請求。 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const notifications = inject(NotificationService);

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

      // catchError 掛在換發本身而非 switchMap 之後：重送的請求若失敗（例如 500），
      // 那是該請求的問題，不該把使用者踢回登入頁。
      return from(auth.refresh()).pipe(
        catchError((refreshError: unknown) => {
          // 共用同一次換發的每個訂閱者都會走到這裡；只有第一個該通知與導航。
          if (auth.isAuthenticated()) {
            notifications.error('登入已過期，請重新登入。');
            auth.logout(router.url);
          }

          return throwError(() => refreshError);
        }),
        switchMap(() =>
          next(request.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken()}` } })),
        ),
      );
    }),
  );
};
