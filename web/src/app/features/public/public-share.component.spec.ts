import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { ShareService } from '../../core/api/share.service';
import { PublicShareComponent } from './public-share.component';

describe('PublicShareComponent', () => {
  it('renders the public archive terminal and item count', async () => {
    await TestBed.configureTestingModule({
      imports: [PublicShareComponent],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'demo' } } },
        },
        {
          provide: ShareService,
          useValue: {
            getPublic: () =>
              of({
                ownerDisplayName: 'Adam',
                scope: 'Showcase',
                items: [],
              }),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PublicShareComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-public-terminal]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('0 件');
  });
});
