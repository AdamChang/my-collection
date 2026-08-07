import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { DisplayMode, ItemDto, ItemImageDto, PagedResult } from '../models';

export interface ItemSearchOptions {
  search?: string;
  categoryId?: string;
  tags?: string[];
  isShowcased?: boolean;
  page?: number;
  pageSize?: number;
  attributes?: Record<string, string>;

  /** 要求「未設定」的 field key：該欄位不存在／為 null／為空字串都算符合。 */
  missingAttributes?: string[];
}

export interface ItemWritePayload {
  categoryId: string;
  name: string;
  description: string | null;
  tags: string[];
  isShowcased: boolean;
  attributes: Record<string, unknown>;
  acquisition: {
    acquiredAt: string | null;
    amount: number | null;
    currency: string | null;
    vendor: string | null;
  } | null;
  locationId?: string | null;
  displayMode?: DisplayMode | null;
  rating?: number | null;
  storageLocation?: string | null;
}

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);

  search(options: ItemSearchOptions): Observable<PagedResult<ItemDto>> {
    let params = new HttpParams();

    if (options.search) params = params.set('search', options.search);
    if (options.categoryId) params = params.set('categoryId', options.categoryId);
    if (options.isShowcased !== undefined) params = params.set('isShowcased', options.isShowcased);
    if (options.page) params = params.set('page', options.page);
    if (options.pageSize) params = params.set('pageSize', options.pageSize);
    for (const tag of options.tags ?? []) {
      params = params.append('tags', tag);
    }
    for (const [key, value] of Object.entries(options.attributes ?? {})) {
      if (value) {
        params = params.set(`attr.${key}`, value);
      }
    }
    for (const key of options.missingAttributes ?? []) {
      params = params.append('missingAttrs', key);
    }

    return this.http.get<PagedResult<ItemDto>>(`${API_BASE}/items`, { params });
  }

  showcase(page = 1, pageSize = 24): Observable<PagedResult<ItemDto>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<ItemDto>>(`${API_BASE}/showcase`, { params });
  }

  get(id: string): Observable<ItemDto> {
    return this.http.get<ItemDto>(`${API_BASE}/items/${id}`);
  }

  tags(): Observable<string[]> {
    return this.http.get<string[]>(`${API_BASE}/items/tags`);
  }

  platforms(categoryId?: string): Observable<string[]> {
    const params = categoryId ? new HttpParams().set('categoryId', categoryId) : undefined;
    return this.http.get<string[]>(`${API_BASE}/items/platforms`, params ? { params } : {});
  }

  create(payload: ItemWritePayload): Observable<ItemDto> {
    return this.http.post<ItemDto>(`${API_BASE}/items`, payload);
  }

  update(id: string, payload: ItemWritePayload): Observable<ItemDto> {
    return this.http.put<ItemDto>(`${API_BASE}/items/${id}`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/items/${id}`);
  }

  uploadImage(itemId: string, file: File): Observable<ItemImageDto> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ItemImageDto>(`${API_BASE}/items/${itemId}/images`, form);
  }

  deleteImage(itemId: string, imageId: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/items/${itemId}/images/${imageId}`);
  }

  setPrimaryImage(itemId: string, imageId: string): Observable<void> {
    return this.http.post<void>(`${API_BASE}/items/${itemId}/images/${imageId}/primary`, null);
  }
}
