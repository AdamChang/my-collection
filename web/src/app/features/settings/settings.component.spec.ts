import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { ProviderService } from '../../core/api/provider.service';
import { ShareService } from '../../core/api/share.service';
import { TransferService } from '../../core/api/transfer.service';
import { NotificationService } from '../../core/notification.service';
import { SyncJobDto } from '../../core/models';
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

    expect(headers).toContain('略過');
    expect(cells).toContain('7');
  });
});
