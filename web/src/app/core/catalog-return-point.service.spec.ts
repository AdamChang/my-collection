import { TestBed } from '@angular/core/testing';
import { CatalogQuery, EMPTY_CATALOG_QUERY } from './catalog-query';
import { CatalogReturnPointService, MAX_RESTORED_PAGES } from './catalog-return-point.service';

function query(overrides: Partial<CatalogQuery> = {}): CatalogQuery {
  return { ...EMPTY_CATALOG_QUERY, ...overrides };
}

function service(): CatalogReturnPointService {
  TestBed.resetTestingModule();

  return TestBed.inject(CatalogReturnPointService);
}

describe('CatalogReturnPointService', () => {
  beforeEach(() => sessionStorage.clear());
  afterAll(() => sessionStorage.clear());

  const ps5 = query({ attributes: { platform: 'PS5' } });

  it('resumes the page count and the anchor for the same filters', () => {
    const returnPoint = service();
    returnPoint.remember(ps5, 3);
    returnPoint.rememberAnchor('item-7');

    expect(returnPoint.resume(ps5)).toEqual({ pages: 3, anchorItemId: 'item-7' });
  });

  /**
   * 這是整個機制唯一會默默給出錯誤結果的分支：套用了別組條件的頁數與錨點不會拋錯、
   * 不會空白，只會顯示一份看起來很合理但不對的清單。
   */
  it('resumes nothing when the filters are not the ones that were remembered', () => {
    const returnPoint = service();
    returnPoint.remember(ps5, 3);
    returnPoint.rememberAnchor('item-7');

    expect(returnPoint.resume(query({ attributes: { platform: 'Switch' } }))).toEqual({
      pages: 1,
      anchorItemId: null,
    });
  });

  /** 同一組條件在網址上的排列順序不保證一致，順序不該讓返回點失效。 */
  it('treats the same filters written in a different order as the same list', () => {
    const returnPoint = service();
    const written = query({ tags: ['RPG', 'SLG'], search: 'zelda' });
    const read = query({ search: 'zelda', tags: ['SLG', 'RPG'] });

    returnPoint.remember(written, 2);

    expect(returnPoint.resume(read).pages).toBe(2);
  });

  /** 錨點回答的是「你剛剛離開時在哪」。回來之後這個問題就沒有了。 */
  it('spends the anchor on the way back', () => {
    const returnPoint = service();
    returnPoint.remember(ps5, 1);
    returnPoint.rememberAnchor('item-7');

    returnPoint.resume(ps5);

    expect(returnPoint.resume(ps5).anchorItemId).toBeNull();
  });

  /**
   * ctrl／中鍵點卡片會觸發 rememberAnchor，但 RouterLink 刻意不為修飾鍵導覽——
   * 使用者留在列表上、手裡卻多了一個錨點。接著改篩選，那個錨點不該跟著過去。
   */
  it('does not carry an anchor from one list to another', () => {
    const returnPoint = service();
    const switchOnly = query({ attributes: { platform: 'Switch' } });

    returnPoint.remember(ps5, 1);
    returnPoint.rememberAnchor('item-7');

    returnPoint.resume(switchOnly);
    returnPoint.remember(switchOnly, 1);

    expect(returnPoint.resume(switchOnly).anchorItemId).toBeNull();
  });

  /** 兩組不同的條件若壓成同一個字串，就會互相還原對方的頁數與錨點。 */
  it('tells apart two lists whose filters flatten to the same text', () => {
    const returnPoint = service();

    returnPoint.remember(query({ search: 'x&tags=RPG' }), 3);

    expect(returnPoint.resume(query({ search: 'x', tags: ['RPG'] })).pages).toBe(1);
  });

  it('tells apart one tag containing a comma from two tags', () => {
    const returnPoint = service();

    returnPoint.remember(query({ tags: ['a,b'] }), 3);

    expect(returnPoint.resume(query({ tags: ['a', 'b'] })).pages).toBe(1);
  });

  /**
   * 小數頁數會一路傳到 `page=3.5`，後端 `int.TryParse` 失敗後退回第 1 頁，
   * 於是前 24 筆被再追加一次，`@for … track item.id` 撞上重複的 key。
   */
  it('discards a stored page count that is not a whole number', () => {
    sessionStorage.setItem(
      'mycollection.catalog.returnPoint',
      JSON.stringify({ queryKey: '', params: {}, pages: 2.5, anchorItemId: null }),
    );

    expect(service().resume(EMPTY_CATALOG_QUERY)).toEqual({ pages: 1, anchorItemId: null });
  });

  /** 後端 pageSize 上限 200，一次請求最多拿得回 8 × 24 = 192 筆。 */
  it('never asks for more pages than one request can carry', () => {
    const returnPoint = service();
    returnPoint.remember(ps5, 40);

    expect(returnPoint.resume(ps5).pages).toBe(MAX_RESTORED_PAGES);
  });

  it('survives the tab it was written in', () => {
    service().remember(ps5, 5);

    expect(service().resume(ps5).pages).toBe(5);
  });

  /** 形狀可能來自上一版的參數設計，或使用者手改過 sessionStorage。 */
  it('discards a stored point it cannot read', () => {
    sessionStorage.setItem('mycollection.catalog.returnPoint', '{"pages":"three"}');

    expect(service().resume(ps5)).toEqual({ pages: 1, anchorItemId: null });
    expect(service().queryParams()).toEqual({});
  });

  it('offers the remembered filters as the way back', () => {
    const returnPoint = service();
    returnPoint.remember(query({ search: 'zelda', missingAttributes: ['platform'] }), 1);

    expect(returnPoint.queryParams()).toEqual({ search: 'zelda', missingAttrs: ['platform'] });
  });

  /** 記憶的寫入點就是狀態變更點，所以清除篩選不需要額外的清記憶邏輯。 */
  it('forgets the filters once they are cleared', () => {
    const returnPoint = service();
    returnPoint.remember(ps5, 3);

    returnPoint.remember(EMPTY_CATALOG_QUERY, 1);

    expect(returnPoint.queryParams()).toEqual({});
  });
});
