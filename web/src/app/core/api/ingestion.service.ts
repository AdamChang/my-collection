import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { ExternalAccountDto, FetchedMetadataDto, SyncJobDto } from '../models';

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

  fetchByUrl(url: string): Observable<FetchedMetadataDto> {
    return this.http.post<FetchedMetadataDto>(`${API_BASE}/ingest/fetch`, null, {
      params: new HttpParams().set('url', url),
    });
  }
}
