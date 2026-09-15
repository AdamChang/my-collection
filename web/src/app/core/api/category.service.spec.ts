import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { CategoryService } from './category.service';

describe('CategoryService', () => {
  let service: CategoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CategoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts a field rename and returns the moved count', async () => {
    const pending = firstValueFrom(service.renameField('c1', 'price', 'purchasePrice'));

    const request = http.expectOne('/api/categories/c1/fields/price/rename');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ newKey: 'purchasePrice' });

    request.flush({
      category: { id: 'c1', name: '公仔', icon: 'box', kind: 'Physical', isSystem: false, defaultDisplayMode: 'List', fields: [] },
      movedItemCount: 12,
    });

    const result = await pending;
    expect(result.movedItemCount).toBe(12);
    expect(result.category.id).toBe('c1');
  });
});
