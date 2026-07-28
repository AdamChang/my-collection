import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { ShowcaseComponent } from './showcase.component';

describe('ShowcaseComponent', () => {
  it('renders the archive terminal and useful empty state', async () => {
    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: { showcase: () => of({ items: [], total: 0, page: 1, pageSize: 24 }) },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShowcaseComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-showcase-terminal]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.showcase__empty a')?.getAttribute('href'))
      .toBe('/catalog');
  });
});
