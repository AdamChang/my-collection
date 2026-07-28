import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
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
});
