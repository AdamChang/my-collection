import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from './models';
import { NotificationService } from './notification.service';

/**
 * 把 RFC 9457 ProblemDetails 轉成可讀訊息。401 與換發失敗交給 authInterceptor 處理：
 * 換發失敗回的是 403 加一句後端的技術訊息，對使用者要說的是「請重新登入」。
 */
/**
 * 讓單一請求宣告「這類錯誤由頁面自己說明」，跳過 toast。錯誤仍會往外拋。
 * 用 predicate 而不是 boolean：頁面通常只想接手特定狀態碼（例如註冊的 403），
 * 其餘錯誤（400、409、連線失敗）仍該由這裡統一顯示。
 */
// 建構參數是「產生預設值的 factory」，預設值本身又是 predicate，所以是兩層箭頭
export const SUPPRESS_ERROR_TOAST = new HttpContextToken<(error: HttpErrorResponse) => boolean>(
  () => () => false,
);

export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const notifications = inject(NotificationService);
  const isRefresh = request.url.includes('/auth/refresh');
  const isSuppressed = request.context.get(SUPPRESS_ERROR_TOAST);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status !== 401 &&
        !isRefresh &&
        !isSuppressed(error)
      ) {
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
