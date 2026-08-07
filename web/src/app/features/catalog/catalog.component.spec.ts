import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CatalogService, ItemSearchOptions } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { CategoryDto } from '../../core/models';
import { CatalogComponent } from './catalog.component';

const gameCategory: CategoryDto = {
  id: 'game',
  name: '實體遊戲',
  icon: 'gamepad-2',
  kind: 'Physical',
  isSystem: true,
  defaultDisplayMode: 'List',
  fields: [
    {
      key: 'platform',
      label: '平台',
      type: 'Text',
      options: null,
      required: false,
      searchable: true,
      showOnCard: true,
    },
  ],
};

function render(categories: CategoryDto[] = []) {
  const searches: ItemSearchOptions[] = [];

  TestBed.configureTestingModule({
    imports: [CatalogComponent],
    providers: [
      provideRouter([]),
      {
        provide: CatalogService,
        useValue: {
          tags: () => of([]),
          platforms: () => of(['Switch']),
          search: (options: ItemSearchOptions) => {
            searches.push(options);
            return of({ items: [], total: 0, page: 1, pageSize: 24 });
          },
        },
      },
      { provide: CategoryService, useValue: { list: () => of(categories) } },
    ],
  });

  const fixture = TestBed.createComponent(CatalogComponent);
  fixture.detectChanges();

  return { fixture, searches };
}

describe('CatalogComponent', () => {
  it('renders filters as a control panel and keeps the create action', () => {
    const { fixture } = render();

    expect(fixture.nativeElement.querySelector('[data-catalog-controls]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('a[href="/items/new"]')).toBeTruthy();
  });

  it('asks for platform-less items and drops any platform value when 未設定 is ticked', () => {
    const { fixture, searches } = render([gameCategory]);
    const component = fixture.componentInstance;

    component.setAttributeFilter('platform', 'Switch');
    component.toggleMissingFilter('platform');

    const latest = searches.at(-1)!;
    expect(latest.missingAttributes).toEqual(['platform']);
    expect(latest.attributes?.['platform']).toBe('');
  });

  it('stops asking for platform-less items once the filter is no longer available', () => {
    const { fixture, searches } = render([gameCategory]);
    const component = fixture.componentInstance;

    component.toggleMissingFilter('platform');
    component.onCategoryChange('no-such-category');

    expect(searches.at(-1)!.missingAttributes).toEqual([]);
  });
});
