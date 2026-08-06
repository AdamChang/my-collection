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
                collageSlotCount: 4,
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

  it('never renders a storage location even though the internal fixture data leaks it', async () => {
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
                collageSlotCount: 4,
                items: [
                  {
                    id: 'i1',
                    name: '初音ミク 1/8',
                    description: null,
                    categoryName: '公仔模型',
                    tags: [],
                    images: [],
                    attributes: {},
                    // storageLocation 刻意不存在於 PublicItemDto（ADR-0008）——就算未來有人不小心塞了值，
                    // toPublicShowcaseDisplayItem 也會把它硬編成 null，這裡驗證 DOM 真的沒有洩漏。
                    cardFields: [],
                    effectiveDisplayMode: 'Hero',
                    price: null,
                    acquiredAt: null,
                    rating: null,
                  },
                ],
              }),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PublicShareComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-hero-storage-location]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('storageLocation');
  });
});
