import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CategoryService } from '../../core/api/category.service';
import { NotificationService } from '../../core/notification.service';
import { CategoriesComponent } from './categories.component';

describe('CategoriesComponent', () => {
  it('renders system categories as read-only and custom categories as editable', async () => {
    await TestBed.configureTestingModule({
      imports: [CategoriesComponent],
      providers: [
        {
          provide: CategoryService,
          useValue: {
            list: () =>
              of([
                { id: 's1', name: '實體遊戲', icon: 'gamepad-2', kind: 'Physical', isSystem: true, fields: [] },
                { id: 'c1', name: '公仔', icon: 'box', kind: 'Physical', isSystem: false, fields: [] },
              ]),
          },
        },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CategoriesComponent);
    fixture.detectChanges();

    const system = fixture.nativeElement.querySelector('[data-system-category]');
    const custom = fixture.nativeElement.querySelector('[data-custom-category]');

    expect(system.textContent).toContain('唯讀');
    expect(system.querySelector('button')).toBeNull();
    expect(custom.querySelector('button')).toBeTruthy();
  });
});
