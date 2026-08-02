import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { ProviderService } from '../../core/api/provider.service';
import { NotificationService } from '../../core/notification.service';
import { SyncJobDto } from '../../core/models';
import { IgdbEnrichComponent } from './igdb-enrich.component';

describe('IgdbEnrichComponent', () => {
  const job: SyncJobDto = {
    id: 'j1', provider: 'igdb', status: 'Succeeded',
    created: 0, updated: 12, failed: 0, skipped: 3,
    error: null, startedAt: '2026-08-01T03:00:00Z', finishedAt: '2026-08-01T03:00:09Z',
  };

  // useValue 餵的是假服務，型別不必完全吻合真實簽章。
  async function create(available: boolean, ingestion: unknown, notifications: unknown = { success: () => undefined }) {
    await TestBed.configureTestingModule({
      imports: [IgdbEnrichComponent],
      providers: [
        { provide: IngestionService, useValue: ingestion },
        { provide: ProviderService, useValue: { supports: () => available } },
        { provide: NotificationService, useValue: notifications },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(IgdbEnrichComponent);
    fixture.detectChanges();

    return fixture;
  }

  it('renders nothing when igdb is not configured', async () => {
    const fixture = await create(false, {});

    expect(fixture.nativeElement.querySelector('[data-igdb-enrich]')).toBeNull();
  });

  it('reports how many items were updated, skipped and failed', async () => {
    const messages: string[] = [];
    const fixture = await create(true, { enrich: () => of(job) }, {
      success: (m: string) => messages.push(m),
    });

    fixture.nativeElement.querySelector('[data-igdb-enrich-run]').click();

    expect(messages[0]).toContain('更新 12');
    expect(messages[0]).toContain('略過 3');
    expect(messages[0]).toContain('失敗 0');
  });

  /** 失敗的補完也會留下一筆 job 紀錄，設定頁兩條路徑都要重載那張表。 */
  it('signals completion so the caller can reload the job table', async () => {
    const fixture = await create(true, { enrich: () => of(job) });

    let completed = 0;
    fixture.componentInstance.completed.subscribe(() => (completed += 1));

    fixture.nativeElement.querySelector('[data-igdb-enrich-run]').click();

    expect(completed).toBe(1);
  });

  it('locks the button while the run is in flight', async () => {
    const pending = new Subject<SyncJobDto>();
    const fixture = await create(true, { enrich: () => pending });

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-enrich-run]');
    button.click();
    fixture.detectChanges();

    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('補完中');
  });
});
