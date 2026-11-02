import { TestBed } from '@angular/core/testing';
import {
  HttpClient,
  HttpContext,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { NotificationService } from './notification.service';
import { SUPPRESS_ERROR_TOAST, errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let notifications: jasmine.SpyObj<NotificationService>;

  beforeEach(() => {
    notifications = jasmine.createSpyObj<NotificationService>('NotificationService', ['error']);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: NotificationService, useValue: notifications },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  /** 送出請求並以指定狀態碼失敗，回傳被拋出的錯誤。firstValueFrom 會立即訂閱，所以 expectOne 找得到請求。 */
  function failWith(status: number, context = new HttpContext()): Promise<unknown> {
    const outcome = firstValueFrom(http.post('/api/auth/register', {}, { context })).then(
      () => fail('expected the request to fail'),
      (error: unknown) => error,
    );
    controller
      .expectOne('/api/auth/register')
      .flush({ detail: 'problem detail' }, { status, statusText: 'Error' });
    return outcome;
  }

  const suppress403 = () =>
    new HttpContext().set(SUPPRESS_ERROR_TOAST, (error) => error.status === 403);

  it('toasts the ProblemDetails detail by default', async () => {
    await failWith(403);

    expect(notifications.error).toHaveBeenCalledOnceWith('problem detail');
  });

  it('skips the toast but still rethrows when the request suppresses that status', async () => {
    const error = await failWith(403, suppress403());

    expect(notifications.error).not.toHaveBeenCalled();
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(403);
  });

  it('still toasts statuses the predicate does not claim', async () => {
    await failWith(400, suppress403());

    expect(notifications.error).toHaveBeenCalledOnceWith('problem detail');
  });
});
