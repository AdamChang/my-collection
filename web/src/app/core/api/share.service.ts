import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { PublicShareDto, ShareLinkDto } from '../models';

export interface ShareWritePayload {
  scope: 'Showcase' | 'Category';
  includeCategoryIds: string[];
  includePrice: boolean;
  expiresAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class ShareService {
  private readonly http = inject(HttpClient);

  list(): Observable<ShareLinkDto[]> {
    return this.http.get<ShareLinkDto[]>(`${API_BASE}/shares`);
  }

  create(payload: ShareWritePayload): Observable<ShareLinkDto> {
    return this.http.post<ShareLinkDto>(`${API_BASE}/shares`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/shares/${id}`);
  }

  /** 匿名端點：authInterceptor 不會附加 token，因為使用者可能未登入。 */
  getPublic(slug: string): Observable<PublicShareDto> {
    return this.http.get<PublicShareDto>(`${API_BASE}/public/${slug}`);
  }
}
