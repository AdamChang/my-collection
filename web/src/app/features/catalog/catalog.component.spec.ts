import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { CatalogComponent } from './catalog.component';

describe('CatalogComponent', () => {
  it('renders filters as a control panel and keeps the create action', async () => {
    await TestBed.configureTestingModule({
      imports: [CatalogComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: {
            tags: () => of([]),
            search: () => of({ items: [], total: 0, page: 1, pageSize: 24 }),
          },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CatalogComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-catalog-controls]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('a[href="/items/new"]')).toBeTruthy();
  });
});
