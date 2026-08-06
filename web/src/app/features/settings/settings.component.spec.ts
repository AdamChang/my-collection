import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject, of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { IGDB_PROVIDER_KEY, ProviderService } from '../../core/api/provider.service';
import { ShareService } from '../../core/api/share.service';
import { TransferService } from '../../core/api/transfer.service';
import { NotificationService } from '../../core/notification.service';
import { SyncJobDto } from '../../core/models';
import { ProviderAccountComponent } from './provider-account.component';
import { ProviderEnrichComponent } from './provider-enrich.component';
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

    expect(fixture.nativeElement.querySelectorAll('[data-settings-panel]').length).toBe(5);
  });

  it('does not lock one account panel while the other is working', async () => {
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

    const panels = fixture.debugElement.queryAll(By.directive(ProviderAccountComponent));
    expect(panels.map((p) => p.componentInstance.provider())).toEqual(['steam', 'psn']);

    const psnForm: HTMLFormElement = panels[1].nativeElement.querySelector('form');
    psnForm.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const steamSubmit: HTMLButtonElement =
      panels[0].nativeElement.querySelector('button[type="submit"]');
    const psnSubmit: HTMLButtonElement =
      panels[1].nativeElement.querySelector('button[type="submit"]');

    expect(psnSubmit.disabled).toBeTrue();
    expect(steamSubmit.disabled).toBeFalse();
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

  it('sends the rating and collage slot count preferences when creating a share', async () => {
    const calls: unknown[][] = [];

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        { provide: IngestionService, useValue: { accounts: () => of([]), jobs: () => of([]) } },
        {
          provide: ShareService,
          useValue: {
            list: () => of([]),
            create: (...args: unknown[]) => {
              calls.push(args);
              return of({});
            },
          },
        },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    fixture.componentInstance.includeRating = true;
    fixture.componentInstance.collageSlotCount = 7;

    fixture.nativeElement.querySelector('[data-create-share]').click();

    expect(calls).toEqual([
      [{ scope: 'Showcase', includeCategoryIds: [], includePrice: false, includeRating: true, collageSlotCount: 7, expiresAt: null }],
    ]);
  });

  it('shows the rating and collage slot count of existing shares', async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        { provide: IngestionService, useValue: { accounts: () => of([]), jobs: () => of([]) } },
        {
          provide: ShareService,
          useValue: {
            list: () =>
              of([
                {
                  id: 's1', slug: 'abc', scope: 'Showcase', includeCategoryIds: [],
                  includePrice: false, includeRating: true, collageSlotCount: 6,
                  expiresAt: null, createdAt: '2026-08-01T00:00:00Z',
                },
              ]),
          },
        },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.querySelector('ul').textContent;
    expect(text).toContain('含評分');
    expect(text).toContain('照片牆 6 格');
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

    const panels = fixture.debugElement.queryAll(By.directive(ProviderEnrichComponent));
    expect(panels.map((p) => p.componentInstance.provider())).toEqual(['igdb', 'steam']);

    panels[1].componentInstance.completed.emit();

    expect(jobs).toHaveBeenCalledTimes(2);
  });
});
