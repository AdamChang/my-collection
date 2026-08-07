import { Injectable, computed, signal } from '@angular/core';
import { Params } from '@angular/router';
import { CatalogQuery, catalogQueryKey, toCatalogQueryParams } from './catalog-query';

/** 使用者上一次離開庫存列表時的位置。 */
export interface ReturnPoint {
  /** 當時生效的篩選條件，已正規化成可比對的字串。 */
  queryKey: string;
  /** 回去時要組出的網址參數。 */
  params: Params;
  /** 當時已載入的頁數。 */
  pages: number;
  /** 使用者點進去的那一筆品項。回到列表後即用罄。 */
  anchorItemId: string | null;
}

/** 還原時要求的頁數上限。後端 pageSize 上限 200，8 × 24 = 192 是單一請求拿得回來的最大值。 */
export const MAX_RESTORED_PAGES = 8;

const STORAGE_KEY = 'mycollection.catalog.returnPoint';

@Injectable({ providedIn: 'root' })
export class CatalogReturnPointService {
  private readonly point = signal<ReturnPoint | null>(read());

  /** 導覽列的「庫存」與品項頁的「返回列表」都用它組出目的地。 */
  readonly queryParams = computed<Params>(() => this.point()?.params ?? {});

  /**
   * 每次篩選變更或載入更多都覆寫。因為寫入點就是狀態變更點，「清除全部篩選」
   * 不需要任何額外的清記憶邏輯——它自己就會把記憶覆寫成無篩選。
   */
  remember(query: CatalogQuery, pages: number): void {
    const queryKey = catalogQueryKey(query);
    const current = this.point();

    this.write({
      queryKey,
      params: toCatalogQueryParams(query),
      pages,
      // 錨點屬於它被記下時的那組條件。條件換了它就不再有意義——留著會讓使用者回到
      // 一個他從未在這組篩選下點開過的位置。ctrl／中鍵點卡片就會留下這種錨點：
      // rememberAnchor 觸發了，但 RouterLink 不為修飾鍵導覽，使用者仍在列表上。
      anchorItemId: current?.queryKey === queryKey ? current.anchorItemId : null,
    });
  }

  rememberAnchor(itemId: string): void {
    const current = this.point();
    if (current) {
      this.write({ ...current, anchorItemId: itemId });
    }
  }

  /**
   * 回到列表時取用返回點。條件與記憶不一致就當作全新的列表——否則使用者手改網址
   * 換了篩選，卻套用上一組篩選的頁數與錨點，畫面會是一份看起來合理但不對的清單。
   *
   * 錨點用過即銷毀：它回答的是「你剛剛離開時在哪」，回來之後這個問題就沒有了。
   */
  resume(query: CatalogQuery): { pages: number; anchorItemId: string | null } {
    const current = this.point();

    if (!current || current.queryKey !== catalogQueryKey(query)) {
      return { pages: 1, anchorItemId: null };
    }

    this.write({ ...current, anchorItemId: null });

    return {
      pages: Math.min(Math.max(current.pages, 1), MAX_RESTORED_PAGES),
      anchorItemId: current.anchorItemId,
    };
  }

  private write(point: ReturnPoint): void {
    this.point.set(point);

    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(point));
    } catch {
      // 無痕模式或配額用盡時寫不進去。記憶掉了就是回到未篩選的列表，不值得中斷操作。
    }
  }
}

/**
 * 讀不回來就當作沒有返回點。形狀可能來自上一版的參數設計，或使用者手改過 sessionStorage
 * ——沿用 `parseShowcaseView` 對壞值的立場：一律退回預設，不讓它壞掉畫面。
 */
function read(): ReturnPoint | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    const parsed: unknown = JSON.parse(raw);
    if (!isReturnPoint(parsed)) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

function isReturnPoint(value: unknown): value is ReturnPoint {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<ReturnPoint>;

  return (
    typeof candidate.queryKey === 'string' &&
    typeof candidate.params === 'object' &&
    candidate.params !== null &&
    // 必須是整數：小數會一路傳到 `page=3.5`，後端 int.TryParse 失敗後退回第 1 頁，
    // 於是同一批品項被追加第二次，@for 的 track item.id 撞上重複的 key。
    Number.isInteger(candidate.pages) &&
    (candidate.pages as number) >= 1 &&
    (candidate.anchorItemId === null || typeof candidate.anchorItemId === 'string')
  );
}
