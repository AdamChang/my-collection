import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
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
}
