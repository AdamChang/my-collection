import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { CatalogService } from './catalog.service';

describe('CatalogService', () => {
  let service: CatalogService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CatalogService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('builds a search query with only the supplied filters', () => {
    firstValueFrom(service.search({ search: 'portal', tags: ['FPS', '最愛'], page: 2, pageSize: 12 }));

    const request = http.expectOne((r) => r.url === '/api/items');
    expect(request.request.params.get('search')).toBe('portal');
    expect(request.request.params.getAll('tags')).toEqual(['FPS', '最愛']);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('12');
    expect(request.request.params.has('categoryId')).toBe(false);
    request.flush({ items: [], total: 0, page: 2, pageSize: 12 });
  });

  it('posts an item create payload', () => {
    firstValueFrom(
      service.create({
        categoryId: 'c1',
        name: '公仔',
        description: null,
        tags: [],
        isShowcased: false,
        attributes: { brand: 'GSC' },
        acquisition: null,
      }),
    );

    const request = http.expectOne('/api/items');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.attributes).toEqual({ brand: 'GSC' });
    request.flush({});
  });

  it('uploads an image as multipart form data', () => {
    firstValueFrom(service.uploadImage('i1', new File(['x'], 'a.png', { type: 'image/png' })));

    const request = http.expectOne('/api/items/i1/images');
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBe(true);
    request.flush({});
  });

  it('fetches the showcase wall', () => {
    firstValueFrom(service.showcase(1, 24));

    const request = http.expectOne((r) => r.url === '/api/showcase');
    expect(request.request.params.get('pageSize')).toBe('24');
    request.flush({ items: [], total: 0, page: 1, pageSize: 24 });
  });
});
