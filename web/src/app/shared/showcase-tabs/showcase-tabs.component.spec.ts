import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ShowcaseTab, ShowcaseTabsComponent } from './showcase-tabs.component';
import { ShowcaseView } from './showcase-view';

const tabs: ShowcaseTab[] = [
  { id: 'collage', label: '拼貼牆', count: 5 },
  { id: 'hero', label: '焦點展品', count: 0 },
  { id: 'stats', label: '遊戲成就', count: 2 },
  { id: 'list', label: '列表', count: 5 },
];

async function createFixture(active: ShowcaseView = 'collage') {
  await TestBed.configureTestingModule({ imports: [ShowcaseTabsComponent] }).compileComponents();

  const fixture = TestBed.createComponent(ShowcaseTabsComponent);
  fixture.componentRef.setInput('tabs', tabs);
  fixture.componentRef.setInput('active', active);
  fixture.detectChanges();

  return fixture;
}

function buttons(fixture: ComponentFixture<ShowcaseTabsComponent>): HTMLButtonElement[] {
  return Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]'));
}

describe('ShowcaseTabsComponent', () => {
  it('renders a tablist with one tab per entry and marks the active one', async () => {
    const fixture = await createFixture('stats');
    const all = buttons(fixture);

    expect(fixture.nativeElement.querySelector('[role="tablist"]')).toBeTruthy();
    expect(all.length).toBe(4);
    expect(all.map((b) => b.getAttribute('aria-selected'))).toEqual([
      'false',
      'false',
      'true',
      'false',
    ]);
  });

  it('shows each tab count and disables the ones with no items', async () => {
    const fixture = await createFixture();
    const all = buttons(fixture);

    expect(all[0].textContent).toContain('5');
    expect(all[1].textContent).toContain('0');
    expect(all[1].disabled).toBeTrue();
    expect(all[0].disabled).toBeFalse();
  });

  it('keeps a roving tabindex so Tab enters the tablist only once', async () => {
    const fixture = await createFixture('stats');

    expect(buttons(fixture).map((b) => b.getAttribute('tabindex'))).toEqual([
      '-1',
      '-1',
      '0',
      '-1',
    ]);
  });

  it('emits the next enabled tab on ArrowRight, skipping disabled ones', async () => {
    const fixture = await createFixture('collage');
    const emitted: string[] = [];
    fixture.componentInstance.activeChange.subscribe((v) => emitted.push(v));

    // collage → (hero 停用，跳過) → stats
    buttons(fixture)[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    fixture.detectChanges();

    expect(emitted).toEqual(['stats']);
  });

  it('wraps around on ArrowLeft from the first tab', async () => {
    const fixture = await createFixture('collage');
    const emitted: string[] = [];
    fixture.componentInstance.activeChange.subscribe((v) => emitted.push(v));

    buttons(fixture)[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    fixture.detectChanges();

    expect(emitted).toEqual(['list']);
  });

  it('jumps to the first and last enabled tab with Home and End', async () => {
    const fixture = await createFixture('stats');
    const emitted: string[] = [];
    fixture.componentInstance.activeChange.subscribe((v) => emitted.push(v));

    buttons(fixture)[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'Home' }));
    buttons(fixture)[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'End' }));
    fixture.detectChanges();

    expect(emitted).toEqual(['collage', 'list']);
  });

  it('emits on click but never for a disabled tab', async () => {
    const fixture = await createFixture();
    const emitted: string[] = [];
    fixture.componentInstance.activeChange.subscribe((v) => emitted.push(v));

    buttons(fixture)[3].click();
    buttons(fixture)[1].click(); // 停用，不該發出
    fixture.detectChanges();

    expect(emitted).toEqual(['list']);
  });
});
