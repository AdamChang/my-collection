import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { CategoryDto, CategoryFieldDto } from '../models';

export interface CategoryWritePayload {
  name: string;
  icon: string;
  kind: 'Physical' | 'Digital';
  fields: CategoryFieldDto[];
}

@Injectable({ providedIn: 'root' })
export class CategoryService {
  private readonly http = inject(HttpClient);

  list(): Observable<CategoryDto[]> {
    return this.http.get<CategoryDto[]>(`${API_BASE}/categories`);
  }

  create(payload: CategoryWritePayload): Observable<CategoryDto> {
    return this.http.post<CategoryDto>(`${API_BASE}/categories`, payload);
  }

  update(id: string, payload: CategoryWritePayload): Observable<CategoryDto> {
    return this.http.put<CategoryDto>(`${API_BASE}/categories/${id}`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/categories/${id}`);
  }
}
