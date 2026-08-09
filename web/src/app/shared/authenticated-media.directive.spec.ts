import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpInterceptorFn, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { EMPTY } from 'rxjs';
import { loadingInterceptor } from '../core/loading.interceptor';
import { AuthenticatedMediaDirective } from './authenticated-media.directive';

@Component({
  imports: [AuthenticatedMediaDirective],
  template: `<img [appAuthenticatedMedia]="source" alt="cover" />`,
})
class HostComponent {
  source = '';
}

describe('AuthenticatedMediaDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads private API media through HttpClient', () => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:private-cover');
    fixture.componentInstance.source = '/api/media/owner/item/card.webp';
    fixture.detectChanges();

    http.expectOne('/api/media/owner/item/card.webp').flush(new Blob(['image']));

    expect((fixture.nativeElement.querySelector('img') as HTMLImageElement).src).toContain(
      'blob:private-cover',
    );
  });

  it('does not send external or public media through HttpClient', () => {
    fixture.componentInstance.source = '/api/public/demo/media/owner/item/card.webp';
    fixture.detectChanges();

    http.expectNone('/api/public/demo/media/owner/item/card.webp');
    expect((fixture.nativeElement.querySelector('img') as HTMLImageElement).getAttribute('src')).toBe(
      '/api/public/demo/media/owner/item/card.webp',
    );
  });
});

/**
 * 指令在 effect 裡發請求。攔截器鏈若在那個反應式環境中讀到任何 signal，
 * 指令就會跟著那個 signal 重跑——重跑先取消原請求再發新的，於是同一張圖被無限重打。
 * 這裡連著真正的 loadingInterceptor 驗，因為它就是踩過這顆地雷的那一個。
 */
describe('AuthenticatedMediaDirective with the real interceptor chain', () => {
  const SOURCE = '/api/media/owner/item/card.webp';
  const REQUEST_LIMIT = 20;

  let issued = 0;

  /** 安全閥：迴歸時讓失控迴圈自行收斂，而不是把測試瀏覽器凍死。 */
  const circuitBreaker: HttpInterceptorFn = (request, next) => {
    issued++;
    return issued > REQUEST_LIMIT ? EMPTY : next(request);
  };

  let fixture: ComponentFixture<HostComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    issued = 0;

    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideHttpClient(withInterceptors([circuitBreaker, loadingInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    fixture = TestBed.createComponent(HostComponent);
    http = TestBed.inject(HttpTestingController);
  });

  function settle(respond: (request: TestRequest) => void): void {
    fixture.componentInstance.source = SOURCE;
    fixture.detectChanges();

    for (let i = 0; i < REQUEST_LIMIT; i++) {
      for (const request of http.match(SOURCE)) {
        if (!request.cancelled) {
          respond(request);
        }
      }

      fixture.detectChanges();
    }
  }

  it('requests missing media exactly once', () => {
    settle((request) => request.flush(null, { status: 404, statusText: 'Not Found' }));

    expect(issued).toBe(1);
  });

  it('requests media that loads exactly once', () => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:private-cover');
    settle((request) => request.flush(new Blob(['image'])));

    expect(issued).toBe(1);
  });
});
