import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
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
});
