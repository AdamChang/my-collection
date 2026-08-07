import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RatingBarComponent } from './rating-bar.component';

function createFixture(rating: number | null = null) {
  const fixture = TestBed.createComponent(RatingBarComponent);
  fixture.componentRef.setInput('rating', rating);
  fixture.detectChanges();

  return fixture;
}

/** 10 個半格熱區，index 0 = 第 1 顆左半（1 分）… index 9 = 第 5 顆右半（10 分）。 */
function halves(fixture: ComponentFixture<RatingBarComponent>): HTMLElement[] {
  return Array.from(fixture.nativeElement.querySelectorAll('[data-rating-half]'));
}

function slider(fixture: ComponentFixture<RatingBarComponent>): HTMLElement {
  return fixture.nativeElement.querySelector('[role="slider"]');
}

/** 每顆星的填色寬度百分比，0 = 空、50 = 半、100 = 滿。 */
function fills(fixture: ComponentFixture<RatingBarComponent>): number[] {
  const nodes: HTMLElement[] = Array.from(
    fixture.nativeElement.querySelectorAll('[data-rating-fill]'),
  );
  return nodes.map((n) => parseFloat(n.style.width));
}

function emissions(fixture: ComponentFixture<RatingBarComponent>): (number | null)[] {
  const emitted: (number | null)[] = [];
  fixture.componentInstance.ratingChange.subscribe((v) => emitted.push(v));
  return emitted;
}

function press(fixture: ComponentFixture<RatingBarComponent>, key: string): void {
  slider(fixture).dispatchEvent(new KeyboardEvent('keydown', { key }));
  fixture.detectChanges();
}

describe('RatingBarComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [RatingBarComponent] }).compileComponents();
  });

  /**
   * 這是整個元件唯一真正的邏輯：半格 index → 1–10 分。
   * 掠射錯一格不會有任何錯誤訊息，只會靜默存錯分數，所以左右半各釘一次。
   */
  it('maps the left half of a star to the odd score and the right half to the even one', () => {
    const fixture = createFixture(null);
    const emitted = emissions(fixture);

    halves(fixture)[6].click(); // 第 4 顆左半
    halves(fixture)[7].click(); // 第 4 顆右半

    expect(emitted).toEqual([7, 8]);
  });

  it('fills whole, half and empty stars from the current rating', () => {
    const fixture = createFixture(7);

    expect(fills(fixture)).toEqual([100, 100, 100, 50, 0]);
  });

  it('renders every star empty when the item is unrated', () => {
    const fixture = createFixture(null);

    expect(fills(fixture)).toEqual([0, 0, 0, 0, 0]);
    expect(fixture.nativeElement.textContent).toContain('未評分');
  });

  it('previews the hovered score on the stars and the label without emitting', () => {
    const fixture = createFixture(6);
    const emitted = emissions(fixture);

    halves(fixture)[7].dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();

    expect(fills(fixture)).toEqual([100, 100, 100, 100, 0]);
    expect(fixture.nativeElement.textContent).toContain('8 / 10');
    expect(emitted).toEqual([]);
  });

  it('restores the actual rating when the pointer leaves', () => {
    const fixture = createFixture(6);

    halves(fixture)[7].dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();
    slider(fixture).dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();

    expect(fills(fixture)).toEqual([100, 100, 100, 0, 0]);
    expect(fixture.nativeElement.textContent).toContain('6 / 10');
  });

  it('steps the score by one with the arrow keys', () => {
    const fixture = createFixture(6);
    const emitted = emissions(fixture);

    press(fixture, 'ArrowRight');
    press(fixture, 'ArrowLeft');

    expect(emitted).toEqual([7, 5]);
  });

  it('starts at 1 when an arrow key is pressed on an unrated item', () => {
    const fixture = createFixture(null);
    const emitted = emissions(fixture);

    press(fixture, 'ArrowRight');

    expect(emitted).toEqual([1]);
  });

  /** 到頂到底不該回捲——評分不是循環的，捲過去會讓 10 分一鍵變 1 分。 */
  it('clamps at the ends instead of wrapping around', () => {
    const top = createFixture(10);
    const topEmitted = emissions(top);
    press(top, 'ArrowRight');

    const bottom = createFixture(1);
    const bottomEmitted = emissions(bottom);
    press(bottom, 'ArrowLeft');

    expect(topEmitted).toEqual([]);
    expect(bottomEmitted).toEqual([]);
  });

  it('jumps to the lowest and highest score with Home and End', () => {
    const fixture = createFixture(6);
    const emitted = emissions(fixture);

    press(fixture, 'Home');
    press(fixture, 'End');

    expect(emitted).toEqual([1, 10]);
  });

  it('clears the rating with Delete and Backspace', () => {
    const del = createFixture(6);
    const delEmitted = emissions(del);
    press(del, 'Delete');

    const back = createFixture(6);
    const backEmitted = emissions(back);
    press(back, 'Backspace');

    expect(delEmitted).toEqual([null]);
    expect(backEmitted).toEqual([null]);
  });

  it('offers a clear button only once the item has a rating', () => {
    const unrated = createFixture(null);
    const rated = createFixture(6);
    const emitted = emissions(rated);

    rated.nativeElement.querySelector('[data-rating-clear]').click();

    expect(unrated.nativeElement.querySelector('[data-rating-clear]')).toBeNull();
    expect(emitted).toEqual([null]);
  });

  /**
   * 星星本身對螢幕閱讀器隱藏，語意全靠容器上的 slider。
   * 這組屬性掉了，這個欄位對鍵盤與輔助技術使用者就等於消失。
   */
  it('exposes the score through the slider role for assistive tech', () => {
    const fixture = createFixture(7);
    const bar = slider(fixture);

    expect(bar.getAttribute('tabindex')).toBe('0');
    expect(bar.getAttribute('aria-valuemin')).toBe('1');
    expect(bar.getAttribute('aria-valuemax')).toBe('10');
    expect(bar.getAttribute('aria-valuenow')).toBe('7');
    expect(bar.getAttribute('aria-valuetext')).toBe('7 分，滿分 10 分');
  });

  it('reports an unrated item as having no value rather than zero', () => {
    const fixture = createFixture(null);
    const bar = slider(fixture);

    expect(bar.getAttribute('aria-valuenow')).toBeNull();
    expect(bar.getAttribute('aria-valuetext')).toBe('未評分');
  });
});
