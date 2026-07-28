import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { LoadingService } from './loading.service';
import { loadingInterceptor } from './loading.interceptor';

describe('loadingInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let loading: LoadingService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([loadingInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    loading = TestBed.inject(LoadingService);
  });

  // ignoreCancelled 只放行明確取消的請求，未 flush 的請求仍會被抓出來。
  afterEach(() => controller.verify({ ignoreCancelled: true }));

  it('is idle before anything is requested', () => {
    expect(loading.isBusy()).toBe(false);
  });

  it('is busy while a request is in flight', () => {
    http.get('/api/items').subscribe();

    expect(loading.isBusy()).toBe(true);

    controller.expectOne('/api/items').flush({});
  });

  it('goes idle once the request succeeds', () => {
    http.get('/api/items').subscribe();
    controller.expectOne('/api/items').flush({});

    expect(loading.isBusy()).toBe(false);
  });

  /** 失敗路徑漏掉歸零的徵狀是「進度條永遠不消失」，比不顯示更像當機。 */
  it('goes idle once the request fails', async () => {
    const result = firstValueFrom(http.get('/api/items')).catch(() => undefined);
    controller.expectOne('/api/items').flush(null, { status: 500, statusText: 'Server Error' });
    await result;

    expect(loading.isBusy()).toBe(false);
  });

  it('goes idle when the caller unsubscribes before the response arrives', () => {
    const subscription = http.get('/api/items').subscribe();

    subscription.unsubscribe();

    expect(loading.isBusy()).toBe(false);
  });

  it('stays busy until the last of several overlapping requests finishes', () => {
    http.get('/api/items').subscribe();
    http.get('/api/categories').subscribe();

    controller.expectOne('/api/items').flush({});
    expect(loading.isBusy()).toBe(true);

    controller.expectOne('/api/categories').flush({});
    expect(loading.isBusy()).toBe(false);
  });
});
