import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
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
