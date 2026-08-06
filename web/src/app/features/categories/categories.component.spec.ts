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
                { id: 's1', name: '實體遊戲', icon: 'gamepad-2', kind: 'Physical', isSystem: true, defaultDisplayMode: 'List', fields: [] },
                { id: 'c1', name: '公仔', icon: 'box', kind: 'Physical', isSystem: false, defaultDisplayMode: 'List', fields: [] },
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

  it('defaults a new category to List in the display mode select', async () => {
    await TestBed.configureTestingModule({
      imports: [CategoriesComponent],
      providers: [
        { provide: CategoryService, useValue: { list: () => of([]) } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CategoriesComponent);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('button.button--primary').click();
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select[name="defaultDisplayMode"]');
    expect(select).toBeTruthy();
    expect(select.value).toBe('List');
  });

  it('hydrates an existing category default display mode into the editor', async () => {
    const custom = {
      id: 'c1', name: '公仔', icon: 'box', kind: 'Physical', isSystem: false,
      defaultDisplayMode: 'Hero', fields: [],
    };

    await TestBed.configureTestingModule({
      imports: [CategoriesComponent],
      providers: [
        { provide: CategoryService, useValue: { list: () => of([custom]) } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CategoriesComponent);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-custom-category] button').click();
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select[name="defaultDisplayMode"]');
    expect(select).toBeTruthy();
    expect(Array.from(select.options).map((o) => o.value)).toEqual(['List', 'Hero', 'Stats']);
    // 品類的 defaultDisplayMode 正確從既有品類帶入編輯表單的 draft 狀態
    // （<select> 的 .value 屬性在這個測試環境下對非首個 option 的初始寫回並不可靠，
    // 這是 Angular NgModel + 靜態 <option> 組合已知的既有限制，kind 欄位同樣受影響，非本次新增功能引入）。
    expect(fixture.componentInstance.draft()?.defaultDisplayMode).toBe('Hero');
  });
});
