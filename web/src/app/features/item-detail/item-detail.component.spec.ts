import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { IngestionService } from '../../core/api/ingestion.service';
import { NotificationService } from '../../core/notification.service';
import { CategoryDto } from '../../core/models';
import { ItemDetailComponent } from './item-detail.component';

describe('ItemDetailComponent', () => {
  const schemaCategory: CategoryDto = {
    id: 'figures',
    name: '模型',
    icon: 'robot',
    kind: 'Physical',
    isSystem: true,
    fields: [
      {
        key: 'brand',
        label: '廠商',
        type: 'Text',
        options: null,
        required: false,
        searchable: false,
        showOnCard: false,
      },
    ],
  };

  it('groups a new item into terminal panels', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-item-core]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.detail__fetch')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('button[type="submit"]')).toBeTruthy();
  });

  it('keeps one editor form when the selected category has schema fields', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([schemaCategory]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.categoryId = schemaCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-item-schema]')).toBeTruthy();
    expect(fixture.nativeElement.querySelectorAll('form').length).toBe(1);
  });

  it('keeps the tag input focus indicator visible', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    const tagInput: HTMLInputElement = fixture.nativeElement.querySelector('app-tag-input input');

    tagInput.focus();

    expect(tagInput.matches(':focus-visible')).toBeTrue();
    expect(getComputedStyle(tagInput).outlineColor).toBe('rgb(32, 231, 255)');
    expect(getComputedStyle(tagInput).outlineStyle).toBe('solid');
  });

  /**
   * 沒有這道鎖，連點三下儲存就是三個 POST、三筆重複品項。
   * 這是正確性問題，不只是觀感。
   */
  it('locks the save button while the create request is in flight', async () => {
    const create = new Subject<unknown>();

    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([schemaCategory]) } },
        { provide: CatalogService, useValue: { create: () => create } },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.categoryId = schemaCategory.id;
    fixture.componentInstance.name = '鋼彈';
    fixture.detectChanges();

    const save: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(save.disabled).toBeFalse();

    save.click();
    fixture.detectChanges();

    expect(save.disabled).toBeTrue();
    expect(save.textContent).toContain('儲存中');
  });

  it('re-enables the fetch button after the lookup fails', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
        { provide: CatalogService, useValue: {} },
        {
          provide: IngestionService,
          useValue: { fetchByUrl: () => throwError(() => new Error('502')) },
        },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.fetchUrl = 'https://example.com/p/1';
    fixture.detectChanges();

    const fetch: HTMLButtonElement = fixture.nativeElement.querySelector('.detail__fetch button');
    fetch.click();
    fixture.detectChanges();

    expect(fetch.disabled).toBeFalse();
    expect(fetch.textContent).toContain('擷取');
  });

  it('stacks URL fetch and acquisition fields in a 390px viewport', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([schemaCategory]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.categoryId = schemaCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    const frame = document.createElement('iframe');
    frame.style.width = '390px';
    frame.style.height = '844px';
    frame.style.border = '0';
    document.body.append(frame);

    try {
      const frameDocument = frame.contentDocument!;
      const styles = frameDocument.createElement('style');
      styles.textContent = Array.from(document.styleSheets)
        .flatMap((sheet) => Array.from(sheet.cssRules))
        .map((rule) => rule.cssText)
        .join('\n');
      frameDocument.head.append(styles);
      frameDocument.body.append(fixture.nativeElement.cloneNode(true));

      const frameWindow = frame.contentWindow!;
      const fetchPanel = frameDocument.querySelector('.detail__fetch')!;
      const acquisition = frameDocument.querySelector('.detail__acquisition')!;

      expect(frameWindow.getComputedStyle(fetchPanel).display).toBe('grid');
      expect(frameWindow.getComputedStyle(acquisition).gridTemplateColumns.split(' ')).toHaveSize(1);
    } finally {
      frame.remove();
    }
  });
});
