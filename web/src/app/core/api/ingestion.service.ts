import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, concatMap, last, map, of, take, takeWhile, timer } from 'rxjs';
import { API_BASE } from '../api-base';
import { ExternalAccountDto, FetchedMetadataDto, ProviderDto, SyncJobDto } from '../models';

@Injectable({ providedIn: 'root' })
export class IngestionService {
  private readonly http = inject(HttpClient);

  accounts(): Observable<ExternalAccountDto[]> {
    return this.http.get<ExternalAccountDto[]>(`${API_BASE}/external-accounts`);
  }

  link(provider: string, externalUserId: string, apiKey: string): Observable<ExternalAccountDto> {
    return this.http.post<ExternalAccountDto>(`${API_BASE}/external-accounts`, {
      provider,
      externalUserId,
      apiKey,
    });
  }

  unlink(provider: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/external-accounts/${provider}`);
  }

  sync(provider: string): Observable<SyncJobDto> {
    return this.http.post<SyncJobDto>(`${API_BASE}/ingest/sync/${provider}`, null);
  }

  jobs(limit = 20): Observable<SyncJobDto[]> {
    return this.http.get<SyncJobDto[]>(`${API_BASE}/ingest/jobs`, {
      params: new HttpParams().set('limit', limit),
    });
  }

  retry(jobId: string): Observable<SyncJobDto> {
    return this.http.post<SyncJobDto>(`${API_BASE}/ingest/jobs/${jobId}/retry`, null);
  }

  fetchByUrl(url: string): Observable<FetchedMetadataDto> {
    return this.http.post<FetchedMetadataDto>(`${API_BASE}/ingest/fetch`, null, {
      params: new HttpParams().set('url', url),
    });
  }

  providers(): Observable<ProviderDto[]> {
    return this.http.get<ProviderDto[]>(`${API_BASE}/ingest/providers`);
  }

  search(provider: string, query: string, limit = 20): Observable<FetchedMetadataDto[]> {
    return this.http.get<FetchedMetadataDto[]>(`${API_BASE}/ingest/search`, {
      params: new HttpParams().set('provider', provider).set('q', query).set('limit', limit),
    });
  }

  /**
   * 不給 itemIds 是批次補完。注意**空陣列也是批次**——後端判斷的是 Count > 0，
   * 所以 enrich(p, []) 不是「什麼都不做」，而是會去補完 50 筆使用者沒選的品項。
   * 之後若接上勾選 UI，呼叫端要自己擋掉空陣列。
   */
  enrich(provider: string, itemIds?: string[]): Observable<SyncJobDto> {
    return this.http.post<SyncJobDto>(`${API_BASE}/ingest/enrich/${provider}`, {
      itemIds: itemIds ?? null,
    });
  }

  /**
   * enrich 的回應在兩種部署下語意不同：本機 in-process 時作業已跑完；Cloud Run 上
   * 走 Cloud Tasks，回應時作業才剛排入，Status 仍是 Running 且統計數字全是 0。
   * 呼叫端若直接拿 Running 的統計判斷結果，會把「還沒開始」誤報成「查無對應」。
   *
   * 這裡把差異收掉：Running 就輪詢 /ingest/jobs 直到作業結束。沒有單筆 job 端點，
   * 所以從最近清單裡撈；剛建立的作業必在最前面。輪詢上限到了仍未結束就回最後一次
   * 看到的快照（Status 仍是 Running），由呼叫端決定怎麼告知使用者，這裡不擲錯——
   * 逾時不是失敗，作業仍會在背景完成。
   */
  awaitJob(job: SyncJobDto, intervalMs = 1500, maxPolls = 20): Observable<SyncJobDto> {
    if (job.status !== 'Running') {
      return of(job);
    }

    return timer(intervalMs, intervalMs).pipe(
      take(maxPolls),
      // concatMap 而非 switchMap：慢回應不該被下一拍取消，否則永遠看不到結果。
      concatMap(() => this.jobs()),
      map((recent) => recent.find((candidate) => candidate.id === job.id) ?? job),
      takeWhile((snapshot) => snapshot.status === 'Running', true),
      last(),
    );
  }
}
