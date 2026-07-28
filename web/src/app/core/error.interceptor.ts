import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from './models';
import { NotificationService } from './notification.service';

/**
 * 把 RFC 9457 ProblemDetails 轉成可讀訊息。401 與換發失敗交給 authInterceptor 處理：
 * 換發失敗回的是 403 加一句後端的技術訊息，對使用者要說的是「請重新登入」。
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const notifications = inject(NotificationService);
  const isRefresh = request.url.includes('/auth/refresh');

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status !== 401 && !isRefresh) {
        notifications.error(describe(error));
      }

      return throwError(() => error);
    }),
  );
};

/**
 * 給 subscribe 的 error 回呼用。訊息已經由上面的 errorInterceptor 顯示成 toast，
 * 元件不必再處理；但少了 error 回呼，RxJS 會把錯誤往外拋成未處理錯誤。
 */
export const IGNORE_HANDLED_BY_INTERCEPTOR = (): void => undefined;

function describe(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return '無法連線到伺服器。';
  }

  const problem = error.error as ProblemDetails | null;

  if (problem?.errors) {
    const messages = Object.entries(problem.errors)
      .map(([field, texts]) => `${field}: ${texts.join('、')}`)
      .join('\n');
    return messages || (problem.title ?? '請求失敗。');
  }

  return problem?.detail ?? problem?.title ?? `請求失敗（HTTP ${error.status}）。`;
}
