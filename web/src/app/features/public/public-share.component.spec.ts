import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ShareService } from '../../core/api/share.service';
import { PublicShareComponent } from './public-share.component';

function publicItem(overrides: Record<string, unknown> = {}) {
  const id = (overrides['id'] as string) ?? 'i1';

  return {
    id,
    name: id,
    description: null,
    categoryName: '公仔模型',
    tags: [],
    images: [],
    attributes: {},
    cardFields: [],
    effectiveDisplayMode: 'List',
    price: null,
    acquiredAt: null,
    rating: null,
    ...overrides,
  };
}

async function createPublicShare(
  items: ReturnType<typeof publicItem>[],
): Promise<ComponentFixture<PublicShareComponent>> {
  await TestBed.configureTestingModule({
    imports: [PublicShareComponent],
    providers: [
      // provideRouter 提供 Router（selectView 寫回 ?view= 要用），ActivatedRoute 再用 mock 蓋掉。
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'demo' } } } },
      {
        provide: ShareService,
        useValue: {
          getPublic: () =>
            of({ ownerDisplayName: 'Adam', scope: 'Showcase', collageSlotCount: 4, items }),
        },
      },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(PublicShareComponent);
  fixture.detectChanges();

  return fixture;
}

describe('PublicShareComponent', () => {
  it('renders the public archive terminal and item count', async () => {
    const fixture = await createPublicShare([]);

    expect(fixture.nativeElement.querySelector('[data-public-terminal]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('0 件');
  });

  it('never renders a storage location even though the internal fixture data leaks it', async () => {
    // storageLocation 刻意不存在於 PublicItemDto（ADR-0008）——就算未來有人不小心塞了值，
    // toPublicShowcaseDisplayItem 也會把它硬編成 null，這裡驗證 DOM 真的沒有洩漏。
    const fixture = await createPublicShare([
      publicItem({ id: '初音ミク 1/8', effectiveDisplayMode: 'Hero' }),
    ]);

    // 必須先切到焦點頁籤，否則 Hero 分區根本沒被渲染，底下的斷言會變成假通過。
    fixture.componentRef.setInput('view', 'hero');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-hero-storage-location]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('storageLocation');
  });

  it('defaults to the collage tab and renders only that section', async () => {
    const fixture = await createPublicShare([
      publicItem({ id: 'h', effectiveDisplayMode: 'Hero' }),
      publicItem({ id: 'l' }),
    ]);

    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-stats-section]')).toBeNull();
  });

  it('falls back to the collage tab for an unknown view value', async () => {
    const fixture = await createPublicShare([publicItem({ id: 'a' })]);
    fixture.componentRef.setInput('view', 'not-a-view');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeTruthy();
  });

  it('renders every shared item in the list tab', async () => {
    const fixture = await createPublicShare([
      publicItem({ id: 'h', effectiveDisplayMode: 'Hero' }),
      publicItem({ id: 'l' }),
    ]);
    fixture.componentRef.setInput('view', 'list');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-public-card]').length).toBe(2);
  });
});
