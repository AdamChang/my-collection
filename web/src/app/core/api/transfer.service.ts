import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { ImportResultDto } from '../models';

@Injectable({ providedIn: 'root' })
export class TransferService {
  private readonly http = inject(HttpClient);

  export(): Observable<Blob> {
    return this.http.get(`${API_BASE}/export`, { responseType: 'blob' });
  }

  import(archive: File): Observable<ImportResultDto> {
    const body = new FormData();
    body.append('file', archive);

    return this.http.post<ImportResultDto>(`${API_BASE}/import`, body);
  }
}
