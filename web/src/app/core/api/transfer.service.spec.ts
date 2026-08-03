import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { TransferService } from './transfer.service';

describe('TransferService', () => {
  let service: TransferService;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(TransferService);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('requests the export as a blob', () => {
    service.export().subscribe();

    const request = controller.expectOne('/api/images/export');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['zip']));
  });

  it('posts the archive as multipart form data named file', async () => {
    const archive = new File(['zip'], 'images.zip', { type: 'application/zip' });
    const result = firstValueFrom(service.import(archive));

    const request = controller.expectOne('/api/images/import');
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBe(true);
    expect((request.request.body as FormData).get('file')).toBe(archive);

    request.flush({ written: 3, skipped: 1, warnings: [] });

    expect((await result).written).toBe(3);
    expect((await result).skipped).toBe(1);
  });
});
