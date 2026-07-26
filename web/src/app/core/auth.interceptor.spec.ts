import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => controller.verify());

  /**
   * 讓 refresh() 的 promise 鏈與 from(promise) 的 .then 全部跑完。
   * setTimeout 是 macrotask，排在所有 pending microtask 之後，
   * 因此不必猜要 await 幾次 Promise.resolve()。
   */
  const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

  async function signIn(): Promise<void> {
    const promise = auth.login('a@b.c', 'x');
    controller.expectOne('/api/auth/login').flush({
      accessToken: 'access-1',
      refreshToken: 'refresh-1',
      expiresAt: '2026-07-25T03:30:00Z',
      user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
    });
    await promise;
  }

  it('does not attach a header when signed out', () => {
    http.get('/api/items').subscribe();

    expect(controller.expectOne('/api/items').request.headers.has('Authorization')).toBe(false);
  });

  it('attaches the bearer token when signed in', async () => {
    await signIn();

    http.get('/api/items').subscribe();

    expect(controller.expectOne('/api/items').request.headers.get('Authorization'))
      .toBe('Bearer access-1');
  });

  it('never attaches the token to auth endpoints', async () => {
    await signIn();

    http.post('/api/auth/refresh', {}).subscribe();

    expect(controller.expectOne('/api/auth/refresh').request.headers.has('Authorization')).toBe(false);
  });

  it('refreshes once on 401 and retries the original request', async () => {
    await signIn();

    const result = firstValueFrom(http.get<{ ok: boolean }>('/api/items'));

    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });

    controller.expectOne('/api/auth/refresh').flush({
      accessToken: 'access-2',
      refreshToken: 'refresh-2',
      expiresAt: '2026-07-25T04:00:00Z',
      user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
    });

    await settle();

    const retried = controller.expectOne('/api/items');
    expect(retried.request.headers.get('Authorization')).toBe('Bearer access-2');
    retried.flush({ ok: true });

    expect(await result).toEqual({ ok: true });
  });

  it('logs out when the refresh also fails', async () => {
    await signIn();

    firstValueFrom(http.get('/api/items')).catch(() => undefined);

    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/auth/refresh').flush(null, { status: 403, statusText: 'Forbidden' });

    await settle();

    expect(auth.isAuthenticated()).toBe(false);
  });
});
