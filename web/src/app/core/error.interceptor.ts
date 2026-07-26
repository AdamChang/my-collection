import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from './models';
import { NotificationService } from './notification.service';

/** 把 RFC 9457 ProblemDetails 轉成可讀訊息。401 交給 authInterceptor 處理，不在這裡吵。 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const notifications = inject(NotificationService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status !== 401) {
        notifications.error(describe(error));
      }

      return throwError(() => error);
    }),
  );
};

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
