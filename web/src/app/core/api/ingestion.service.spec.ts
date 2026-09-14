import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SyncJobDto } from '../models';
import { IngestionService } from './ingestion.service';

describe('IngestionService', () => {
  let service: IngestionService;
  let http: HttpTestingController;

  const job: SyncJobDto = {
    id: 'j1',
    provider: 'igdb',
    status: 'Succeeded',
    created: 0,
    updated: 1,
    failed: 0,
    skipped: 2,
    error: null,
    startedAt: '2026-08-01T03:00:00Z',
    finishedAt: '2026-08-01T03:00:05Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(IngestionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the registered providers', async () => {
    const pending = firstValueFrom(service.providers());

    const request = http.expectOne('/api/ingest/providers');
    expect(request.request.method).toBe('GET');
    request.flush([{ key: 'igdb', capabilities: 'Search' }]);

    // capabilities 是後端 [Flags] enum 的 ToString()，逗號分隔的字串而非陣列。
    // 下一個 task 的 ProviderService 要解析它，形狀在這裡先釘住。
    expect((await pending)[0].capabilities).toBe('Search');
  });

  it('sends provider, q and limit as query parameters when searching', async () => {
    const pending = firstValueFrom(service.search('igdb', 'the witcher 3'));

    const request = http.expectOne((r) => r.url === '/api/ingest/search');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('provider')).toBe('igdb');
    expect(request.request.params.get('q')).toBe('the witcher 3');
    expect(request.request.params.get('limit')).toBe('20');
    request.flush([]);

    expect(await pending).toEqual([]);
  });

  /**
   * 上一條只證明了預設值是 20。把實作寫死成 .set('limit', 20) 它也會通過，
   * 所以還需要這一條來釘住「呼叫端傳進來的值真的有被送出去」。
   */
  it('sends the caller supplied limit', async () => {
    const pending = firstValueFrom(service.search('igdb', 'x', 5));

    const request = http.expectOne((r) => r.url === '/api/ingest/search');
    expect(request.request.params.get('limit')).toBe('5');
    request.flush([]);

    await pending;
  });

  it('posts itemIds to the provider-scoped enrich route', async () => {
    const pending = firstValueFrom(service.enrich('igdb', ['65b0000000000000000000aa']));

    const request = http.expectOne('/api/ingest/enrich/igdb');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ itemIds: ['65b0000000000000000000aa'] });
    request.flush(job);

    expect((await pending).skipped).toBe(2);
  });

  /** 本層不送 limit，批次筆數由後端決定。 */
  it('sends a null itemIds for a batch run', async () => {
    const pending = firstValueFrom(service.enrich('igdb'));

    const request = http.expectOne('/api/ingest/enrich/igdb');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ itemIds: null });
    request.flush(job);

    await pending;
  });
  describe('awaitJob', () => {
    const running: SyncJobDto = { ...job, status: 'Running', updated: 0, finishedAt: null };

    /** 本機 in-process 時作業已跑完，不該多打任何一次 /jobs。 */
    it('returns a finished job immediately without polling', async () => {
      expect(await firstValueFrom(service.awaitJob(job))).toEqual(job);
      http.expectNone('/api/ingest/jobs');
    });

    /**
     * 沒有單筆 job 端點，只能從最近清單裡撈同 id 的那筆。
     * 清單裡別的作業（例如同時在跑的 Steam sync）不能被誤認成結果。
     */
    it('polls the recent jobs until the matching one leaves Running', fakeAsync(() => {
      let result: SyncJobDto | undefined;
      service.awaitJob(running, 1000, 5).subscribe((j) => (result = j));

      tick(1000);
      http.expectOne((r) => r.url === '/api/ingest/jobs').flush([
        { ...job, id: 'other' },
        running,
      ]);
      expect(result).toBeUndefined();

      tick(1000);
      http.expectOne((r) => r.url === '/api/ingest/jobs').flush([{ ...job, id: 'other' }, job]);
      expect(result).toEqual(job);

      // 拿到終態就停，不會再多打一拍。
      tick(1000);
      http.expectNone('/api/ingest/jobs');
    }));

    /**
     * 逾時回最後一次看到的 Running 快照而不是擲錯：作業仍會在背景完成，
     * 呼叫端要的是「還沒好」而不是「壞了」。
     */
    it('gives up after maxPolls and hands back the still-running snapshot', fakeAsync(() => {
      let result: SyncJobDto | undefined;
      service.awaitJob(running, 1000, 2).subscribe((j) => (result = j));

      tick(1000);
      http.expectOne((r) => r.url === '/api/ingest/jobs').flush([]);
      tick(1000);
      http.expectOne((r) => r.url === '/api/ingest/jobs').flush([running]);

      expect(result).toEqual(running);
      tick(1000);
      http.expectNone('/api/ingest/jobs');
    }));
  });
});
