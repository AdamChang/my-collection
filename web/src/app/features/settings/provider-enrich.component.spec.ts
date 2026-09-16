import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { ProviderService } from '../../core/api/provider.service';
import { NotificationService } from '../../core/notification.service';
import { SyncJobDto } from '../../core/models';
import { ProviderEnrichComponent } from './provider-enrich.component';

describe('ProviderEnrichComponent', () => {
  const finished: SyncJobDto = {
    id: 'j1', provider: 'igdb', status: 'Succeeded',
    created: 0, updated: 12, failed: 1, skipped: 3,
    error: null, startedAt: '2026-08-01T03:00:00Z', finishedAt: '2026-08-01T03:00:09Z',
  };

  /** 背景 provider 回應時工作尚未開始，統計數字必然全是 0。 */
  const queued: SyncJobDto = {
    id: 'j2', provider: 'steam', status: 'Running',
    created: 0, updated: 0, failed: 0, skipped: 0,
    error: null, startedAt: '2026-08-04T03:00:00Z', finishedAt: null,
  };

  // useValue 餵的是假服務，型別不必完全吻合真實簽章。
  // awaitJob 預設原樣放行：大多數案例只關心 enrich 的回應怎麼被解讀。
  async function create(
    provider: string,
    supported: string[],
    ingestion: object,
    notifications: unknown = { success: () => undefined },
  ) {
    await TestBed.configureTestingModule({
      imports: [ProviderEnrichComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { awaitJob: (job: SyncJobDto) => of(job), ...ingestion },
        },
        {
          provide: ProviderService,
          useValue: {
            supports: (key: string, capability: string) =>
              supported.includes(key) && capability === 'Enrich',
          },
        },
        { provide: NotificationService, useValue: notifications },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ProviderEnrichComponent);
    fixture.componentRef.setInput('provider', provider);
    fixture.componentRef.setInput('heading', '測試補完');
    fixture.componentRef.setInput('description', '說明');
    fixture.detectChanges();

    return fixture;
  }

  const runButton = (fixture: { nativeElement: HTMLElement }, provider: string) =>
    fixture.nativeElement.querySelector(
      `[data-provider-enrich-run="${provider}"]`,
    ) as HTMLButtonElement;

  it('renders nothing when the provider cannot enrich', async () => {
    const fixture = await create('igdb', [], {});

    expect(fixture.nativeElement.querySelector('[data-provider-enrich]')).toBeNull();
  });

  it('renders only for the provider it was given', async () => {
    const fixture = await create('steam', ['steam'], { enrich: () => of(queued) });

    expect(fixture.nativeElement.querySelector('[data-provider-enrich="steam"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-provider-enrich="igdb"]')).toBeNull();
  });

  it('asks the given provider for a batch run and reports updated, skipped and failed', async () => {
    const messages: string[] = [];
    const calls: unknown[][] = [];
    const fixture = await create(
      'igdb',
      ['igdb'],
      {
        enrich: (...args: unknown[]) => {
          calls.push(args);
          return of(finished);
        },
      },
      { success: (m: string) => messages.push(m) },
    );

    runButton(fixture, 'igdb').click();

    expect(calls).toEqual([['igdb']]);
    expect(messages[0]).toContain('更新 12');
    expect(messages[0]).toContain('略過 3');
    expect(messages[0]).toContain('失敗 1');
  });

  /**
   * Cloud Run 上 enrich 走 Cloud Tasks，回應的 job 是 Running 且統計全零。
   * 直接拿回應報數字會說「更新 0」，而作業幾秒後其實會把 50 筆補完。
   * 結果必須來自 awaitJob 等到的終態，而不是回應本身。
   */
  it('waits for a background job to finish before reporting counts', async () => {
    const messages: string[] = [];
    const awaited: SyncJobDto[] = [];
    const fixture = await create(
      'steam',
      ['steam'],
      {
        enrich: () => of(queued),
        awaitJob: (job: SyncJobDto) => {
          awaited.push(job);
          return of({ ...finished, id: job.id, provider: job.provider });
        },
      },
      { success: (m: string) => messages.push(m) },
    );

    runButton(fixture, 'steam').click();

    expect(awaited.map((j) => j.status)).toEqual(['Running']);
    expect(messages).toEqual(['補完完成：更新 12、略過 3、失敗 1']);
  });

  /**
   * 輪詢逾時不是失敗：作業仍會在背景完成，但也不能報「完成：更新 0」——
   * 使用者會以為沒東西可補，而工作其實還在跑。
   */
  it('tells the user the work is still in the background when polling gives up', async () => {
    const messages: string[] = [];
    const fixture = await create(
      'steam',
      ['steam'],
      { enrich: () => of(queued) },
      { success: (m: string) => messages.push(m) },
    );

    runButton(fixture, 'steam').click();

    expect(messages[0]).toContain('背景作業');
    expect(messages[0]).not.toContain('更新 0');
  });

  /**
   * 批次補完最多 50 次反查加上輪詢，是全站最長的操作；Steam 那張面板還明說「可以離開此頁」。
   * 離開後結果送回來，通知會冒在別的頁面上，completed 更會讓已銷毀的設定頁再打一次 jobs。
   */
  it('stops listening once destroyed mid-run', async () => {
    const messages: string[] = [];
    const pending = new Subject<SyncJobDto>();
    const fixture = await create(
      'igdb',
      ['igdb'],
      { enrich: () => pending },
      { success: (m: string) => messages.push(m) },
    );

    let completed = 0;
    fixture.componentInstance.completed.subscribe(() => (completed += 1));
    // 銷毀後 emit 會被 OutputEmitterRef 吞掉並警告 NG0953——那是 Angular 的防線，
    // 不是這個元件的行為；completed 該在 finalize 之外，銷毀時根本不該走到 emit。
    const warn = spyOn(console, 'warn');

    runButton(fixture, 'igdb').click();
    expect(pending.observed).toBeTrue();

    fixture.destroy();

    expect(pending.observed).toBeFalse();
    pending.next(finished);
    expect(messages).toEqual([]);
    expect(completed).toBe(0);
    expect(warn).not.toHaveBeenCalled();
  });

  /** 失敗若發生在 job 建立之後就會留下紀錄，設定頁兩條路徑都要重載那張表。 */
  it('signals completion so the caller can reload the job table', async () => {
    const fixture = await create('igdb', ['igdb'], { enrich: () => of(finished) });

    let completed = 0;
    fixture.componentInstance.completed.subscribe(() => (completed += 1));

    runButton(fixture, 'igdb').click();

    expect(completed).toBe(1);
  });

  it('signals completion even when the run fails', async () => {
    const fixture = await create('igdb', ['igdb'], { enrich: () => throwError(() => new Error('x')) });

    let completed = 0;
    fixture.componentInstance.completed.subscribe(() => (completed += 1));

    runButton(fixture, 'igdb').click();

    expect(completed).toBe(1);
  });

  it('locks the button while the run is in flight', async () => {
    const pending = new Subject<SyncJobDto>();
    const fixture = await create('igdb', ['igdb'], { enrich: () => pending });

    const button = runButton(fixture, 'igdb');
    button.click();
    fixture.detectChanges();

    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('補完中');
  });

  it('unlocks the button once the run finishes', async () => {
    const pending = new Subject<SyncJobDto>();
    const fixture = await create('igdb', ['igdb'], { enrich: () => pending });

    const button = runButton(fixture, 'igdb');
    button.click();
    fixture.detectChanges();

    pending.next(finished);
    pending.complete();
    fixture.detectChanges();

    expect(button.disabled).toBeFalse();
    expect(button.textContent).toContain('批次補完');
  });
});
