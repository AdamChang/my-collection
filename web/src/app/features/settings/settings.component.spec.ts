import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { ShareService } from '../../core/api/share.service';
import { NotificationService } from '../../core/notification.service';
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
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-settings-panel]').length).toBe(3);
  });
});
