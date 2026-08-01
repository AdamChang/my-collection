import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, finalize, takeUntil } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { IGDB_PROVIDER_KEY } from '../../core/api/provider.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { FetchedMetadataDto } from '../../core/models';

/**
 * 只做三件事：搜尋、讓使用者挑、把挑中的吐出去。
 * 刻意不知道目標品類，也不知道呼叫端是要新增還是要綁定——那是 ItemDetailComponent 的事。
 *
 * 對話框內不放 <form>：ItemDetailComponent 的模板根節點就是 <form>，巢狀 form 是非法 HTML。
 */
@Component({
  selector: 'app-igdb-search-dialog',
  imports: [FormsModule],
  template: `
    <dialog #dialog class="igdb" (close)="onClose()" aria-labelledby="igdb-title">
      <h2 id="igdb-title" class="igdb__title">從 IGDB 搜尋遊戲</h2>
      <div class="igdb__bar">
        <input
          [(ngModel)]="query"
          [ngModelOptions]="{ standalone: true }"
          aria-label="遊戲名稱"
          [disabled]="searching()"
          (keydown.enter)="search()"
          placeholder="遊戲名稱"
          data-igdb-query
        />
        <button
          type="button"
          (click)="search()"
          [disabled]="query.trim().length < 2 || searching()"
          data-igdb-search
        >
          {{ searching() ? '搜尋中…' : '搜尋' }}
        </button>
        <button type="button" (click)="close()">取消</button>
      </div>

      @if (results().length) {
        <ul class="igdb__grid">
          @for (result of results(); track result.externalId) {
            <li>
              <button type="button" (click)="choose(result)" data-igdb-result>
                @if (result.imageUrl) {
                  <img [src]="result.imageUrl" alt="" loading="lazy" />
                } @else {
                  <span class="igdb__nocover">無封面</span>
                }
                <strong>{{ result.name }}</strong>
                @if (subtitle(result); as subtitleText) {
                  <small>{{ subtitleText }}</small>
                }
              </button>
            </li>
          }
        </ul>
      } @else if (searched()) {
        <p class="igdb__empty" data-igdb-empty>查無符合的遊戲。換個關鍵字試試。</p>
      }
    </dialog>
  `,
  styles: `
    .igdb { width: min(46rem, 92vw); border: 1px solid var(--mc-border-strong); background: var(--mc-surface); color: inherit; }
    .igdb::backdrop { background: rgb(0 0 0 / 0.6); }
    .igdb__title { margin: 0 0 0.75rem; font-size: 1.05rem; }
    .igdb__bar { display: flex; gap: 0.5rem; align-items: center; }
    .igdb__bar input { flex: 1; }
    .igdb__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(9rem, 1fr)); gap: 0.75rem; margin: 1rem 0 0; padding: 0; list-style: none; max-height: 60vh; overflow-y: auto; }
    .igdb__grid button { display: grid; gap: 0.35rem; width: 100%; text-align: left; padding: 0.5rem; }
    .igdb__grid img { width: 100%; aspect-ratio: 3 / 4; object-fit: cover; }
    .igdb__nocover { display: grid; place-items: center; aspect-ratio: 3 / 4; border: 1px dashed var(--mc-border); color: var(--mc-text-muted); font-size: 0.8rem; }
    .igdb__grid small { color: var(--mc-text-muted); font-size: 0.78rem; }
    .igdb__empty { margin: 1rem 0 0; color: var(--mc-text-muted); }
    @media (max-width: 30rem) {
      .igdb__bar { display: grid; grid-template-columns: 1fr; }
    }
  `,
})
export class IgdbSearchDialogComponent {
  private readonly ingestion = inject(IngestionService);

  readonly select = output<FetchedMetadataDto>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly results = signal<FetchedMetadataDto[]>([]);
  protected readonly searching = signal(false);

  /** 區分「還沒搜過」與「搜過但沒結果」——只有後者該顯示空狀態。 */
  protected readonly searched = signal(false);

  query = '';

  private readonly closed = new Subject<void>();

  open(): void {
    this.dialog().nativeElement.showModal();
  }

  close(): void {
    this.dialog().nativeElement.close();
    // 不能只靠下面的 (close) 監聽：那是瀏覽器把 'close' 事件排進獨立任務佇列非同步觸發，
    // 相對其他任務來源的先後順序沒有保證。程式呼叫 close()（例如「取消」與 choose()）在這裡
    // 就同步重設，呼叫端不必等一輪事件迴圈就能看到乾淨的狀態。
    this.reset();
  }

  /** Esc 是使用者直接操作原生對話框，元件收不到通知，只能靠這個事件補上重設與取消請求。 */
  protected onClose(): void {
    this.reset();
  }

  /**
   * 少了這個，被放棄的請求回來時會把結果灌進下一輪，而使用者看到的搜尋框是空的——
   * 他會以為那些結果是自己搜的。
   *
   * close() 與 onClose() 都會走到這裡，重複呼叫安全的理由是「close 事件必定早於下一次
   * 使用者互動送達」，不是「必無訂閱者」——同一輪內 close() 之後立刻 search()，
   * 遲到的 close 事件一樣會把新請求砍掉。那條路徑經 UI 不可達，但理由要寫對。
   */
  private reset(): void {
    this.closed.next();
    this.query = '';
    this.results.set([]);
    this.searched.set(false);
  }

  search(): void {
    const query = this.query.trim();

    // 後端 SearchProviderQueryValidator 要求至少兩個字元；單字元對 IGDB 也只會回一堆雜訊。
    // 不在這裡擋的話使用者會收到一則洩漏 DTO 屬性名的英文 400 訊息。
    if (query.length < 2 || this.searching()) {
      return;
    }

    // 錯誤訊息由 errorInterceptor 顯示，這裡只負責解鎖讓使用者能換關鍵字重試。
    this.searching.set(true);
    this.ingestion
      .search(IGDB_PROVIDER_KEY, query)
      .pipe(
        takeUntil(this.closed),
        finalize(() => this.searching.set(false)),
      )
      .subscribe({
        next: (results) => {
          this.results.set(results);
          this.searched.set(true);
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  protected choose(result: FetchedMetadataDto): void {
    this.select.emit(result);
    this.close();
  }

  /** 年份 · 開發商。任一缺席就不留下孤立的分隔符號。 */
  protected subtitle(result: FetchedMetadataDto): string {
    const released = result.attributes['releaseDate'];
    const developer = result.attributes['developer'];

    return [
      typeof released === 'string' ? released.slice(0, 4) : null,
      typeof developer === 'string' ? developer : null,
    ]
      .filter((part): part is string => part !== null)
      .join(' · ');
  }
}
