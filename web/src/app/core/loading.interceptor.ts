import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { LoadingService } from './loading.service';

/**
 * 計數所有 HTTP 請求，不設任何端點例外——例外清單只會製造計數漏歸零的風險，
 * 而漏歸零的徵狀（進度條永遠不消失）比多顯示一次進度條嚴重得多。
 *
 * 註冊在 authInterceptor 之前，因此 401 觸發的換發與重送都涵蓋在同一次計數內。
 * finalize 同時覆蓋成功、失敗與取消訂閱三條路徑。
 */
export const loadingInterceptor: HttpInterceptorFn = (request, next) => {
  const loading = inject(LoadingService);

  loading.start();

  return next(request).pipe(finalize(() => loading.stop()));
};
