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
  async function create(
    provider: string,
    supported: string[],
    ingestion: unknown,
    notifications: unknown = { success: () => undefined },
  ) {
    await TestBed.configureTestingModule({
      imports: [ProviderEnrichComponent],
      providers: [
        { provide: IngestionService, useValue: ingestion },
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
   * 背景作業報「完成：更新 0」會讓使用者以為沒東西可補，而工作其實才剛排進佇列。
   */
  it('tells the user the work was queued when the job is still running', async () => {
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
