import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { NotificationService } from '../../core/notification.service';
import { ExternalAccountDto } from '../../core/models';
import { ProviderAccountComponent } from './provider-account.component';

describe('ProviderAccountComponent', () => {
  const create = async (
    inputs: Record<string, unknown>,
    ingestion: Partial<IngestionService>,
  ): Promise<ComponentFixture<ProviderAccountComponent>> => {
    await TestBed.configureTestingModule({
      imports: [ProviderAccountComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([]), ...ingestion },
        },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ProviderAccountComponent);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();

    return fixture;
  };

  const steamInputs = {
    provider: 'steam',
    heading: 'Steam 帳號',
    userIdLabel: 'SteamID64',
    secretLabel: 'Web API Key',
  };

  const submit = (fixture: ComponentFixture<ProviderAccountComponent>): void => {
    const form: HTMLFormElement = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  };

  it('sends the typed user id and secret for a provider that needs both', async () => {
    const link = jasmine.createSpy('link').and.returnValue(of({} as ExternalAccountDto));
    const fixture = await create(steamInputs, { link });

    fixture.componentInstance.userId = '76561197960287930';
    fixture.componentInstance.secret = 'STEAM_KEY';
    submit(fixture);

    expect(link).toHaveBeenCalledWith('steam', '76561197960287930', 'STEAM_KEY');
  });

  it('clears the secret after a successful link', async () => {
    const fixture = await create(steamInputs, {
      link: () => of({} as ExternalAccountDto),
      accounts: () => of([]),
    });

    fixture.componentInstance.userId = '7656';
    fixture.componentInstance.secret = 'STEAM_KEY';
    submit(fixture);

    expect(fixture.componentInstance.secret).toBe('');
  });

  const psnInputs = {
    provider: 'psn',
    heading: 'PSN 帳號',
    requiresUserId: false,
    secretLabel: 'NPSSO',
  };

  it('sends the fixed user id for a provider that has no user id field', async () => {
    const link = jasmine.createSpy('link').and.returnValue(of({} as ExternalAccountDto));
    const fixture = await create(psnInputs, { link });

    fixture.componentInstance.secret = 'NPSSO_VALUE';
    submit(fixture);

    expect(link).toHaveBeenCalledWith('psn', 'me', 'NPSSO_VALUE');
  });

  it('does not render a user id field when the provider has none', async () => {
    const fixture = await create(psnInputs, {});

    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('label') as NodeListOf<HTMLLabelElement>,
    ).map((label) => label.textContent);

    expect(labels.length).toBe(1);
    expect(labels[0]).toContain('NPSSO');
  });

  it('shows the bound state without the literal user id when the provider has none', async () => {
    const account: ExternalAccountDto = {
      provider: 'psn',
      externalUserId: 'me',
      updatedAt: '2026-08-05T02:30:00Z',
    };
    const fixture = await create(psnInputs, { accounts: () => of([account]) });

    const text: string = fixture.nativeElement.textContent;

    expect(text).toContain('已綁定');
    expect(fixture.nativeElement.querySelector('code')).toBeNull();
  });

  const boundSteam: ExternalAccountDto = {
    provider: 'steam',
    externalUserId: '76561197960287930',
    updatedAt: '2026-08-05T02:30:00Z',
  };

  it('does not offer sync before an account is linked', async () => {
    const fixture = await create(steamInputs, {});

    expect(fixture.nativeElement.querySelector('[data-provider-account-sync]')).toBeNull();
  });

  it('syncs the provider it was given and reports the counts', async () => {
    const sync = jasmine.createSpy('sync').and.returnValue(
      of({
        id: 'j1', provider: 'steam', status: 'Succeeded',
        created: 3, updated: 4, failed: 0, skipped: 0,
        error: null, startedAt: '', finishedAt: '',
      }),
    );
    const success = jasmine.createSpy('success');

    await TestBed.configureTestingModule({
      imports: [ProviderAccountComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([boundSteam]), sync },
        },
        { provide: NotificationService, useValue: { success } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ProviderAccountComponent);
    for (const [key, value] of Object.entries(steamInputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-provider-account-sync]').click();
    fixture.detectChanges();

    expect(sync).toHaveBeenCalledWith('steam');
    expect(success.calls.mostRecent().args[0]).toContain('新增 3');
  });

  it('unlinks the provider it was given', async () => {
    const unlink = jasmine.createSpy('unlink').and.returnValue(of(undefined));
    const fixture = await create(steamInputs, {
      accounts: () => of([boundSteam]),
      unlink,
    });

    fixture.nativeElement.querySelector('[data-provider-account-unlink]').click();
    fixture.detectChanges();

    expect(unlink).toHaveBeenCalledWith('steam');
  });

  it('locks its own submit button while the link request is in flight', async () => {
    const link = new Subject<ExternalAccountDto>();
    const fixture = await create(steamInputs, { link: () => link });

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button.disabled).toBeFalse();

    submit(fixture);

    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('綁定中');
  });

  it('re-enables its own submit button after the link request fails', async () => {
    const link = new Subject<ExternalAccountDto>();
    const fixture = await create(steamInputs, { link: () => link });

    submit(fixture);
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button.disabled).toBeTrue();

    link.error(new Error('500'));
    fixture.detectChanges();

    expect(button.disabled).toBeFalse();
  });

  it('emits changed after a sync so the parent can reload its job log', async () => {
    const changed = jasmine.createSpy('changed');
    const fixture = await create(steamInputs, {
      accounts: () => of([boundSteam]),
      sync: () => of({
        id: 'j1', provider: 'steam', status: 'Succeeded',
        created: 0, updated: 0, failed: 0, skipped: 0,
        error: null, startedAt: '', finishedAt: '',
      }),
    });
    fixture.componentInstance.changed.subscribe(changed);

    fixture.nativeElement.querySelector('[data-provider-account-sync]').click();
    fixture.detectChanges();

    expect(changed).toHaveBeenCalled();
  });
});
