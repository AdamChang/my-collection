import { Injectable, inject, signal } from '@angular/core';
import { catchError, of } from 'rxjs';
import { ProviderDto } from '../models';
import { IngestionService } from './ingestion.service';

/** 對應後端的 ProviderKeys.Igdb。 */
export const IGDB_PROVIDER_KEY = 'igdb';

/** 對應後端的 ProviderKeys.Steam。 */
export const STEAM_PROVIDER_KEY = 'steam';

/** 對應後端 ProviderCapability 這個 [Flags] enum 的成員名稱。 */
export type ProviderCapability = 'BulkSync' | 'UrlLookup' | 'Search' | 'Enrich';

@Injectable({ providedIn: 'root' })
export class ProviderService {
  private readonly ingestion = inject(IngestionService);

  /**
   * 初值為空陣列——「還不知道」與「後端沒註冊」共用同一個狀態。
   * 兩者的正確 UI 都是不顯示入口，所以不需要區分。
   *
   * 探測刻意留在「第一次被注入時」而不是提前到 bootstrap：/ingest 需要授權，
   * 而 App 在登入之前就建構。提前探測會拿到 401，接著被 catchError 吞成空清單，
   * 而這個服務是 singleton、只探測一次——結果是使用者登入後 IGDB 入口永遠不出現。
   * 需要 supports() 的三個入口都在登入後的懶載入路由裡，那時探測才有意義。
   *
   * 代價是按鈕比頁面晚一個 round-trip 出現。
   */
  private readonly providers = signal<ProviderDto[]>([]);

  constructor() {
    this.ingestion
      .providers()
      .pipe(catchError(() => of<ProviderDto[]>([])))
      .subscribe((providers) => this.providers.set(providers));
  }

  supports(key: string, capability: ProviderCapability): boolean {
    const provider = this.providers().find((p) => p.key === key);

    return (
      provider != null &&
      provider.capabilities
        .split(',')
        .map((flag) => flag.trim())
        .includes(capability)
    );
  }
}
