import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject, of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { IGDB_PROVIDER_KEY, ProviderService } from '../../core/api/provider.service';
import { ShareService } from '../../core/api/share.service';
import { TransferService } from '../../core/api/transfer.service';
import { NotificationService } from '../../core/notification.service';
import { SyncJobDto } from '../../core/models';
import { IgdbEnrichComponent } from './igdb-enrich.component';
import { SettingsComponent } from './settings.component';

describe('SettingsComponent', () => {
  it('renders account, sync, and sharing terminal panels', async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([]), jobs: () => of([]) },
        },
        { provide: ShareService, useValue: { list: () => of([]) } },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-settings-panel]').length).toBe(4);
  });

  /** 重複送出的綁定會對同一個 Steam 帳號打出多次寫入。 */
  it('locks the link button while the request is in flight', async () => {
    const link = new Subject<unknown>();

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([]), jobs: () => of([]), link: () => link },
        },
        { provide: ShareService, useValue: { list: () => of([]) } },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    const submit: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(submit.disabled).toBeFalse();

    fixture.componentInstance.link();
    fixture.detectChanges();

    expect(submit.disabled).toBeTrue();
    expect(submit.textContent).toContain('綁定中');
  });

  it('re-enables the share button after the request fails', async () => {
    const create = new Subject<unknown>();

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([]), jobs: () => of([]) },
        },
        { provide: ShareService, useValue: { list: () => of([]), create: () => create } },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    const share: HTMLButtonElement = fixture.nativeElement.querySelector('[data-create-share]');
    share.click();
    fixture.detectChanges();
    expect(share.disabled).toBeTrue();

    create.error(new Error('500'));
    fixture.detectChanges();

    expect(share.disabled).toBeFalse();
  });

  it('shows the skipped count in the sync log', async () => {
    const job: SyncJobDto = {
      id: 'j1', provider: 'igdb', status: 'Succeeded',
      created: 0, updated: 12, failed: 1, skipped: 7,
      error: null, startedAt: '2026-08-01T03:00:00Z', finishedAt: '2026-08-01T03:00:09Z',
    };

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        { provide: IngestionService, useValue: { accounts: () => of([]), jobs: () => of([job]) } },
        { provide: ShareService, useValue: { list: () => of([]) } },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    const headers = Array.from(fixture.nativeElement.querySelectorAll('th')).map(
      (th) => (th as HTMLElement).textContent,
    );
    const cells = Array.from(fixture.nativeElement.querySelectorAll('tbody td')).map(
      (td) => (td as HTMLElement).textContent,
    );

    expect(headers).toEqual(['時間', '來源', '狀態', '新增', '更新', '略過', '失敗']);
    expect(cells.slice(1)).toEqual(['igdb', 'Succeeded', '0', '12', '7', '1']);
  });

  it('uses seven cells for an empty sync log row', async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        { provide: IngestionService, useValue: { accounts: () => of([]), jobs: () => of([]) } },
        { provide: ShareService, useValue: { list: () => of([]) } },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    const emptyCell: HTMLTableCellElement = fixture.nativeElement.querySelector('tbody td');
    expect(emptyCell.textContent).toContain('尚無同步紀錄。');
    expect(emptyCell.colSpan).toBe(7);
  });

  it('reloads sync jobs when the enrich panel completes', async () => {
    const jobs = jasmine.createSpy('jobs').and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        { provide: IngestionService, useValue: { accounts: () => of([]), jobs } },
        { provide: ShareService, useValue: { list: () => of([]) } },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        {
          provide: ProviderService,
          useValue: {
            supports: (key: string, capability: string) =>
              key === IGDB_PROVIDER_KEY && capability === 'Search',
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    const enrich = fixture.debugElement.query(By.directive(IgdbEnrichComponent)).componentInstance;
    expect(enrich).toBeInstanceOf(IgdbEnrichComponent);

    enrich.completed.emit();

    expect(jobs).toHaveBeenCalledTimes(2);
  });
});
