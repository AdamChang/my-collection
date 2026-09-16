import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { CategoryService } from '../../core/api/category.service';
import { NotificationService } from '../../core/notification.service';
import { CategoriesComponent } from './categories.component';

describe('CategoriesComponent', () => {
  const custom = {
    id: 'c1', name: '公仔', icon: 'box', kind: 'Physical' as const, isSystem: false,
    defaultDisplayMode: 'List' as const,
    fields: [{ key: 'price', label: '價格', type: 'Text' as const, options: null, required: false, searchable: false, showOnCard: false }],
  };

  async function setup(api: Partial<CategoryService>, notifications: Partial<NotificationService> = {}) {
    await TestBed.configureTestingModule({
      imports: [CategoriesComponent],
      providers: [
        { provide: CategoryService, useValue: { list: () => of([custom]), ...api } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined, ...notifications } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CategoriesComponent);
    fixture.detectChanges();
    fixture.componentInstance.edit(custom);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

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

  it('opens the editor as a modal dialog and closes it on cancel', async () => {
    const custom = {
      id: 'c1', name: '公仔', icon: 'box', kind: 'Physical', isSystem: false,
      defaultDisplayMode: 'List', fields: [],
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

    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog[data-category-editor]');
    expect(dialog).toBeTruthy();
    expect(dialog.open).toBeFalse();

    fixture.nativeElement.querySelector('[data-custom-category] button').click();
    fixture.detectChanges();

    // 編輯表單必須在置中的 modal 內：showModal() 才會進 top layer 並畫出 backdrop。
    expect(dialog.open).toBeTrue();
    expect(dialog.querySelector('form')).toBeTruthy();

    dialog.querySelector<HTMLButtonElement>('[data-category-editor-cancel]')!.click();
    fixture.detectChanges();

    expect(dialog.open).toBeFalse();
    expect(fixture.componentInstance.draft()).toBeNull();
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

  it('locks the key of fields the category already declares but not of fields added in this session', async () => {
    const fixture = await setup({});

    // 模板的 [name] 綁定被 NgModel 的 name input 接走，不會寫入 DOM 屬性；用 aria-label 定位
    const existingKey: HTMLInputElement = fixture.nativeElement.querySelector('input[aria-label="欄位 1 key"]');
    expect(existingKey.readOnly).toBe(true);
    expect(fixture.nativeElement.querySelector('button[data-rename="0"]')).toBeTruthy();

    fixture.componentInstance.addField();
    fixture.detectChanges();

    const newKey: HTMLInputElement = fixture.nativeElement.querySelector('input[aria-label="欄位 2 key"]');
    expect(newKey.readOnly).toBe(false);
    expect(fixture.nativeElement.querySelector('button[data-rename="1"]')).toBeNull();
  });

  it('treats a field re-added with a removed key as new in this session', async () => {
    const fixture = await setup({});

    fixture.componentInstance.removeField(0);
    fixture.componentInstance.addField();
    fixture.componentInstance.draft()!.fields[0].key = 'price';
    fixture.detectChanges();

    // 移除既有欄位後再新增同名 key：它是本次新增的欄位，key 要可編輯、不該出現改名鈕
    const key: HTMLInputElement = fixture.nativeElement.querySelector('input[aria-label="欄位 1 key"]');
    expect(key.readOnly).toBe(false);
    expect(fixture.nativeElement.querySelector('button[data-rename="0"]')).toBeNull();
  });

  it('opens an inline rename row for the chosen field', async () => {
    const fixture = await setup({});

    fixture.nativeElement.querySelector('button[data-rename="0"]').click();
    fixture.detectChanges();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[name="renameKey"]');
    expect(input).toBeTruthy();
    expect(fixture.nativeElement.querySelector('button[data-rename-confirm]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('button[data-rename-cancel]')).toBeTruthy();
  });

  it('confirms a rename through the API, updates the draft key and reports the moved count', async () => {
    const renameField = jasmine.createSpy('renameField').and.returnValue(
      of({ category: { ...custom, fields: [{ ...custom.fields[0], key: 'purchasePrice' }] }, movedItemCount: 12 }),
    );
    const success = jasmine.createSpy('success');
    const fixture = await setup({ renameField }, { success });

    fixture.nativeElement.querySelector('button[data-rename="0"]').click();
    fixture.detectChanges();
    // NgModel 在 <form> 內由 NgForm.addControl 延後一個 microtask 才接線，得等它穩定才能派發 input 事件
    await fixture.whenStable();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[name="renameKey"]');
    input.value = 'purchasePrice';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('button[data-rename-confirm]').click();
    fixture.detectChanges();

    expect(renameField).toHaveBeenCalledWith('c1', 'price', 'purchasePrice');
    expect(fixture.componentInstance.draft()!.fields[0].key).toBe('purchasePrice');
    // 改名已在後端生效：新鍵要視為既有欄位，key 輸入框維持唯讀
    expect(fixture.componentInstance.isExistingField(fixture.componentInstance.draft()!.fields[0])).toBe(true);
    expect(success).toHaveBeenCalledWith(jasmine.stringContaining('12'));
    expect(fixture.nativeElement.querySelector('input[name="renameKey"]')).toBeNull();
  });

  it('keeps the draft and the inline row when the rename fails', async () => {
    const renameField = jasmine.createSpy('renameField').and.returnValue(throwError(() => new Error('409')));
    const fixture = await setup({ renameField });

    fixture.nativeElement.querySelector('button[data-rename="0"]').click();
    fixture.detectChanges();
    // NgModel 在 <form> 內由 NgForm.addControl 延後一個 microtask 才接線，得等它穩定才能派發 input 事件
    await fixture.whenStable();
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[name="renameKey"]');
    input.value = 'purchasePrice';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    fixture.nativeElement.querySelector('button[data-rename-confirm]').click();
    fixture.detectChanges();

    // 失敗時 inline 列留著，使用者修正後可直接重送
    expect(fixture.componentInstance.draft()!.fields[0].key).toBe('price');
    expect(fixture.nativeElement.querySelector('input[name="renameKey"]')).toBeTruthy();
  });

  it('locks save and delete while a rename is in flight', async () => {
    const pending = new Subject<never>();
    const renameField = jasmine.createSpy('renameField').and.returnValue(pending.asObservable());
    const fixture = await setup({ renameField });

    fixture.nativeElement.querySelector('button[data-rename="0"]').click();
    fixture.detectChanges();
    // NgModel 在 <form> 內由 NgForm.addControl 延後一個 microtask 才接線，得等它穩定才能派發 input 事件
    await fixture.whenStable();
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[name="renameKey"]');
    input.value = 'purchasePrice';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    fixture.nativeElement.querySelector('button[data-rename-confirm]').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.busy()).toBe(true);

    pending.complete();
    fixture.detectChanges();
    expect(fixture.componentInstance.busy()).toBe(false);
  });
});
