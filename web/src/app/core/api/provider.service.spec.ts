import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProviderDto } from '../models';
import { IGDB_PROVIDER_KEY, ProviderService } from './provider.service';

describe('ProviderService', () => {
  let http: HttpTestingController;

  /** 只負責組 TestBed 與取得兩個角色，回應由各測試自己決定。 */
  function configure(): ProviderService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpTestingController);

    return TestBed.inject(ProviderService);
  }

  function createWith(providers: ProviderDto[]): ProviderService {
    const service = configure();
    http.expectOne('/api/ingest/providers').flush(providers);

    return service;
  }

  afterEach(() => http.verify());

  it('reports a capability the provider declares', () => {
    const service = createWith([{ key: 'igdb', capabilities: 'Search' }]);

    expect(service.supports(IGDB_PROVIDER_KEY, 'Search')).toBeTrue();
  });

  /** 後端回的是 [Flags] 的 ToString()，多重能力長這樣："BulkSync, UrlLookup"。 */
  it('parses combined capability flags', () => {
    const service = createWith([{ key: 'steam', capabilities: 'BulkSync, UrlLookup' }]);

    expect(service.supports('steam', 'UrlLookup')).toBeTrue();
    expect(service.supports('steam', 'Search')).toBeFalse();
  });

  /**
   * 後端的 ProviderCapability 是 [Flags] enum，日後可能加入名稱互為子字串的成員。
   * 少了這條，把實作換成 capabilities.includes(capability) 也會全部通過，
   * 而那個實作會讓只支援 ImageSearch 的 provider 誤報支援 Search。
   */
  it('does not treat a capability name as a substring match', () => {
    const service = createWith([{ key: 'steam', capabilities: 'ImageSearch' }]);

    expect(service.supports('steam', 'Search')).toBeFalse();
  });

  it('reports false for a provider that is not registered', () => {
    const service = createWith([{ key: 'steam', capabilities: 'BulkSync' }]);

    expect(service.supports(IGDB_PROVIDER_KEY, 'Search')).toBeFalse();
  });

  /**
   * 這是第一次被注入時的背景探測，不是啟動時的請求——見 ProviderService 的註解。
   * 失敗不該跳一則使用者沒有能力處理的錯誤，也不該讓呼叫端拿到例外；
   * 退化成「沒有任何 provider」即可，那與「後端沒註冊」是同一種 UI。
   */
  it('degrades to no providers when the request fails', () => {
    const service = configure();

    http.expectOne('/api/ingest/providers')
      .flush(null, { status: 502, statusText: 'Bad Gateway' });

    expect(service.supports(IGDB_PROVIDER_KEY, 'Search')).toBeFalse();
  });
});
