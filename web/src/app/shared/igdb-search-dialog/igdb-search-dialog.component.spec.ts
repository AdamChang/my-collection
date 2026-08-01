import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { FetchedMetadataDto } from '../../core/models';
import { IgdbSearchDialogComponent } from './igdb-search-dialog.component';

describe('IgdbSearchDialogComponent', () => {
  const witcher: FetchedMetadataDto = {
    provider: 'igdb',
    externalId: '1942',
    name: 'The Witcher 3: Wild Hunt',
    description: 'A story-driven adventure.',
    imageUrl: 'https://images.igdb.com/igdb/image/upload/t_cover_big/co1wyy.jpg',
    attributes: {
      igdbId: 1942,
      developer: 'CD Projekt RED',
      releaseDate: '2015-05-18T00:00:00Z',
    },
  };

  // useValue 餵的是假服務，型別不必完全吻合真實簽章。
  async function createWith(ingestion: unknown) {
    await TestBed.configureTestingModule({
      imports: [IgdbSearchDialogComponent],
      providers: [{ provide: IngestionService, useValue: ingestion }],
    }).compileComponents();

    const fixture = TestBed.createComponent(IgdbSearchDialogComponent);
    fixture.detectChanges();
    fixture.componentInstance.open();
    fixture.detectChanges();

    return fixture;
  }

  it('trims the query before sending it', async () => {
    const search = jasmine.createSpy('search').and.returnValue(of([]));
    const fixture = await createWith({ search });

    fixture.componentInstance.query = '  the witcher 3  ';
    fixture.componentInstance.search();

    expect(search).toHaveBeenCalledWith('igdb', 'the witcher 3');
  });

  /** 後端 SearchProviderQueryValidator 要求至少兩個字元，前端得先擋掉。 */
  it('does not send a query shorter than the server minimum', async () => {
    const search = jasmine.createSpy('search').and.returnValue(of([]));
    const fixture = await createWith({ search });

    fixture.componentInstance.query = 'a';
    fixture.componentInstance.search();

    expect(search).not.toHaveBeenCalled();
  });

  it('renders one selectable card per result', async () => {
    const fixture = await createWith({
      search: () => of([witcher, { ...witcher, externalId: '1943', name: 'Hearts of Stone' }]),
    });

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-igdb-result]').length).toBe(2);
  });

  it('emits the chosen result and closes', async () => {
    const fixture = await createWith({ search: () => of([witcher]) });

    const emitted: FetchedMetadataDto[] = [];
    fixture.componentInstance.select.subscribe((r) => emitted.push(r));

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-igdb-result]').click();
    fixture.detectChanges();

    expect(emitted).toEqual([witcher]);
    expect(fixture.nativeElement.querySelector('dialog').open).toBeFalse();
  });

  /** 查無結果不是錯誤，不走 errorInterceptor。 */
  it('shows an empty state instead of nothing when the search returns no games', async () => {
    const fixture = await createWith({ search: () => of([]) });

    fixture.componentInstance.query = 'zzzz';
    fixture.componentInstance.search();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-igdb-empty]')).toBeTruthy();
  });

  /** 沒有這道鎖，連點三下就是三個請求，而 IGDB 只允許 4 req/sec。 */
  it('locks the search button while a request is in flight', async () => {
    const pending = new Subject<FetchedMetadataDto[]>();
    const fixture = await createWith({ search: () => pending });

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.detectChanges();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-search]');
    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('搜尋中');
  });

  /**
   * 按鈕的 disabled 是外觀，這裡量的是真正重要的東西：請求數。
   * (keydown.enter) 這條路徑不經過按鈕，只有 searching() 早退擋得住。
   */
  it('does not fire a second request while one is in flight', async () => {
    const search = jasmine.createSpy('search').and.returnValue(new Subject<FetchedMetadataDto[]>());
    const fixture = await createWith({ search });

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.componentInstance.search();
    fixture.componentInstance.search();

    expect(search).toHaveBeenCalledTimes(1);
  });

  /**
   * I-1 的核心保護。少了 takeUntil，被放棄的請求回來時會把結果灌進下一輪，
   * 而 searching 要等到那時才解鎖——使用者看到的是空搜尋框配上鎖死的搜尋鈕，
   * 接著憑空冒出一批他沒搜的結果。
   */
  it('discards a response that arrives after the dialog was closed', async () => {
    const pending = new Subject<FetchedMetadataDto[]>();
    const fixture = await createWith({ search: () => pending });

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.componentInstance.close();
    fixture.detectChanges();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-search]');
    expect(button.textContent).not.toContain('搜尋中');

    pending.next([witcher]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-igdb-result]').length).toBe(0);
  });

  /**
   * Esc 不經過元件的 close()，只派發原生 close 事件——(close) 綁定是這條路徑唯一的攔截點。
   * 直接對 DOM 元素呼叫 close() 精確重現這件事：對話框自己關了，元件方法沒被呼叫。
   */
  it('discards a response when the dialog is dismissed with Esc', async () => {
    const pending = new Subject<FetchedMetadataDto[]>();
    const fixture = await createWith({ search: () => pending });
    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();

    const closed = new Promise<void>((resolve) =>
      dialog.addEventListener('close', () => resolve(), { once: true }),
    );
    dialog.close();
    await closed;
    fixture.detectChanges();

    pending.next([witcher]);
    fixture.detectChanges();

    expect(fixture.componentInstance.query).toBe('');
    expect(fixture.nativeElement.querySelectorAll('[data-igdb-result]').length).toBe(0);
  });

  /** open() 的重設現在掛在 close 事件上，這條確保那條線路真的接著。 */
  it('leaves nothing from the previous round when reopened', async () => {
    const fixture = await createWith({ search: () => of([witcher]) });

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-igdb-result]').length).toBe(1);

    fixture.componentInstance.close();
    fixture.detectChanges();
    fixture.componentInstance.open();
    fixture.detectChanges();

    expect(fixture.componentInstance.query).toBe('');
    expect(fixture.nativeElement.querySelectorAll('[data-igdb-result]').length).toBe(0);
    expect(fixture.nativeElement.querySelector('[data-igdb-empty]')).toBeNull();
  });

  /** 兩個欄位任一缺席時不能留下孤立的 ' · '。 */
  it('renders 年份 · 開發商 and drops the separator when one side is missing', async () => {
    const fixture = await createWith({
      search: () => of([
        witcher,
        { ...witcher, externalId: '2', attributes: { developer: 'FromSoftware' } },
        { ...witcher, externalId: '3', attributes: { releaseDate: '2011-06-01T00:00:00Z' } },
      ]),
    });

    fixture.componentInstance.query = 'xx';
    fixture.componentInstance.search();
    fixture.detectChanges();

    const subtitles = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('[data-igdb-result] small'),
    ).map((el) => el.textContent);

    expect(subtitles).toEqual(['2015 · CD Projekt RED', 'FromSoftware', '2011']);
  });

  /**
   * attributes 是 Record<string, unknown>。releaseDate 若哪天變回 IGDB 原始的 epoch 數字，
   * 少了型別守衛就會在畫面上印出 '1431' 這種垃圾年份。
   */
  it('ignores a non-string releaseDate instead of rendering garbage', async () => {
    const fixture = await createWith({
      search: () => of([{ ...witcher, attributes: { releaseDate: 1431907200, developer: 'CD Projekt RED' } }]),
    });

    fixture.componentInstance.query = 'xx';
    fixture.componentInstance.search();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-igdb-result] small').textContent)
      .toBe('CD Projekt RED');
  });
});
