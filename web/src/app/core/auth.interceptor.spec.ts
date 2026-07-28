import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { errorInterceptor } from './error.interceptor';
import { NotificationService } from './notification.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        // 兩個攔截器一起註冊：「換發失敗只吐一則可行動訊息」需要兩者協同才驗得到。
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        // logout() 會真的導航，沒有這條路由 Router 會噴 NG04002 汙染測試輸出。
        provideRouter([{ path: 'login', children: [] }]),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
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

  /**
   * 後端的 refresh token 是 rotation 的：換發成功就作廢舊 token。
   * 每個頁面初始化都平行送 2-3 個請求，若各自換發，第一個轉走 token 後
   * 其餘會拿著已作廢的 token 得到 403，反而把剛換好的 session 清掉。
   */
  it('refreshes only once when several requests fail with 401 together', async () => {
    await signIn();

    const items = firstValueFrom(http.get<{ id: string }>('/api/items'));
    const categories = firstValueFrom(http.get<{ id: string }>('/api/categories'));

    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/categories').flush(null, { status: 401, statusText: 'Unauthorized' });

    const refreshes = controller.match('/api/auth/refresh');
    expect(refreshes.length).toBe(1);

    refreshes[0].flush({
      accessToken: 'access-2',
      refreshToken: 'refresh-2',
      expiresAt: '2026-07-25T04:00:00Z',
      user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
    });

    await settle();

    const retriedItems = controller.expectOne('/api/items');
    const retriedCategories = controller.expectOne('/api/categories');
    expect(retriedItems.request.headers.get('Authorization')).toBe('Bearer access-2');
    expect(retriedCategories.request.headers.get('Authorization')).toBe('Bearer access-2');

    retriedItems.flush({ id: 'i1' });
    retriedCategories.flush({ id: 'c1' });

    expect(await items).toEqual({ id: 'i1' });
    expect(await categories).toEqual({ id: 'c1' });
  });

  it('allows a later refresh once the in-flight one has settled', async () => {
    await signIn();

    firstValueFrom(http.get('/api/items')).catch(() => undefined);
    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/auth/refresh').flush({
      accessToken: 'access-2',
      refreshToken: 'refresh-2',
      expiresAt: '2026-07-25T04:00:00Z',
      user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
    });
    await settle();
    controller.expectOne('/api/items').flush({});

    firstValueFrom(http.get('/api/tags')).catch(() => undefined);
    controller.expectOne('/api/tags').flush(null, { status: 401, statusText: 'Unauthorized' });

    const request = controller.expectOne('/api/auth/refresh');
    expect(request.request.body).toEqual({ refreshToken: 'refresh-2' });
    request.flush(null, { status: 403, statusText: 'Forbidden' });

    await settle();
  });

  it('sends the user to the login page with a return url when the refresh fails', async () => {
    await signIn();
    spyOnProperty(router, 'url', 'get').and.returnValue('/items/abc');
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);

    firstValueFrom(http.get('/api/items')).catch(() => undefined);

    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/auth/refresh').flush(null, { status: 403, statusText: 'Forbidden' });

    await settle();

    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/items/abc' },
    });
  });

  /**
   * 並行的 401 共用同一次換發，換發失敗時每個訂閱者的 catchError 都會跑一次。
   * 沒有守衛的話使用者會看到三則一模一樣的 toast。
   */
  it('announces the expired session once even when several requests were waiting', async () => {
    await signIn();
    const notifications = TestBed.inject(NotificationService);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);

    firstValueFrom(http.get('/api/items')).catch(() => undefined);
    firstValueFrom(http.get('/api/categories')).catch(() => undefined);
    firstValueFrom(http.get('/api/tags')).catch(() => undefined);

    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/categories').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/tags').flush(null, { status: 401, statusText: 'Unauthorized' });

    controller.expectOne('/api/auth/refresh').flush(null, { status: 403, statusText: 'Forbidden' });

    await settle();

    expect(notifications.notifications().length).toBe(1);
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  /** 後端回的是 "Invalid or expired refresh token."，對使用者要說的是「請重新登入」。 */
  it('reports an actionable message instead of the backend refresh error', async () => {
    await signIn();
    const notifications = TestBed.inject(NotificationService);

    firstValueFrom(http.get('/api/items')).catch(() => undefined);

    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/auth/refresh').flush(
      { title: 'Forbidden.', detail: 'Invalid or expired refresh token.' },
      { status: 403, statusText: 'Forbidden' },
    );

    await settle();

    const messages = notifications.notifications().map((n) => n.message);
    expect(messages).toEqual(['登入已過期，請重新登入。']);
  });
});
