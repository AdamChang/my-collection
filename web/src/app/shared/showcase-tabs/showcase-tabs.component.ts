import { Component, ElementRef, effect, input, output, viewChildren } from '@angular/core';
import { ShowcaseView } from './showcase-view';

export interface ShowcaseTab {
  id: ShowcaseView;
  label: string;
  /** 該頁籤底下的品項數。0 代表停用——版面不跳動，但使用者看得到自己還沒用到的展示模式。 */
  count: number;
}

/**
 * 精選頁的展示模式頁籤列，`/showcase` 與 `/p/:slug` 共用。
 *
 * 依 ADR-0009，頁籤是篩選器不是版型選擇器——切換頁籤換的是「看哪一群品項」，
 * 而不是「用什麼版型看同一群品項」。
 *
 * 鍵盤行為做完整的 WAI-ARIA tabs pattern。做一半的 tablist（有 role 沒方向鍵）
 * 對螢幕閱讀器使用者比完全沒有 role 更糟：它宣告自己是頁籤，卻不照頁籤的方式運作。
 */
@Component({
  selector: 'app-showcase-tabs',
  template: `
    <div class="tabs" role="tablist" aria-label="展示模式" data-showcase-tabs>
      @for (tab of tabs(); track tab.id) {
        <button
          #tabButton
          type="button"
          role="tab"
          class="tabs__tab"
          [class.tabs__tab--active]="tab.id === active()"
          [id]="'showcase-tab-' + tab.id"
          [attr.aria-selected]="tab.id === active()"
          [attr.aria-controls]="'showcase-panel-' + tab.id"
          [attr.tabindex]="tab.id === active() ? 0 : -1"
          [disabled]="tab.count === 0"
          (click)="select(tab)"
          (keydown)="onKeydown($event)"
        >
          <span class="tabs__label">{{ tab.label }}</span>
          <span class="tabs__count">{{ tab.count }}</span>
        </button>
      }
    </div>
  `,
  styles: `
    .tabs { display: flex; flex-wrap: wrap; gap: 0.4rem; margin-bottom: 1.25rem;
            border-bottom: 1px solid var(--mc-border); padding-bottom: 0.5rem; }
    .tabs__tab { display: inline-flex; align-items: baseline; gap: 0.45rem; padding: 0.5rem 1rem;
                 font: 700 0.8rem/1.4 Consolas, monospace; letter-spacing: 0.06em; cursor: pointer;
                 color: var(--mc-text-muted); background: var(--mc-surface);
                 border: 1px solid var(--mc-border); transition: color 160ms ease, border-color 160ms ease;
                 clip-path: polygon(0 0, calc(100% - var(--mc-cut)) 0, 100% var(--mc-cut), 100% 100%, 0 100%); }
    .tabs__tab:hover:not(:disabled) { color: var(--mc-text); border-color: var(--mc-border-strong); }
    .tabs__tab:disabled { opacity: 0.4; cursor: not-allowed; }
    .tabs__tab--active { color: var(--mc-cyan); border-color: var(--mc-cyan);
                         background: var(--mc-surface-raised); box-shadow: inset 0 -2px 0 var(--mc-cyan); }
    .tabs__count { font-size: 0.7rem; opacity: 0.75; }
    @media (max-width: 520px) {
      .tabs__tab { padding: 0.45rem 0.7rem; }
    }
  `,
})
export class ShowcaseTabsComponent {
  readonly tabs = input<ShowcaseTab[]>([]);
  readonly active = input.required<ShowcaseView>();
  readonly activeChange = output<ShowcaseView>();

  private readonly tabButtons = viewChildren<ElementRef<HTMLButtonElement>>('tabButton');

  private keyboardNavigated = false;

  /**
   * roving tabindex 讓 Tab 鍵只進入 tablist 一次，之後靠方向鍵在頁籤間移動。
   * 代價是切換後焦點會留在舊按鈕（它已變成 tabindex="-1"），所以要手動把焦點帶過去；
   * 只在鍵盤操作時這麼做，滑鼠點擊不搶焦點。
   */
  constructor() {
    effect(() => {
      const activeId = this.active();

      if (!this.keyboardNavigated) {
        return;
      }

      this.keyboardNavigated = false;
      const index = this.tabs().findIndex((tab) => tab.id === activeId);
      this.tabButtons()[index]?.nativeElement.focus();
    });
  }

  select(tab: ShowcaseTab): void {
    if (tab.count > 0 && tab.id !== this.active()) {
      this.activeChange.emit(tab.id);
    }
  }

  onKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        this.step(1);
        break;
      case 'ArrowLeft':
      case 'ArrowUp':
        this.step(-1);
        break;
      case 'Home':
        this.jumpTo(this.enabledTabs()[0]);
        break;
      case 'End':
        this.jumpTo(this.enabledTabs().at(-1));
        break;
      default:
        return;
    }

    event.preventDefault();
  }

  /** 往前／後找下一個啟用的頁籤並環繞。停用的頁籤要跳過，否則使用者會停在一個按了沒反應的頁籤上。 */
  private step(delta: number): void {
    const tabs = this.tabs();
    const start = tabs.findIndex((tab) => tab.id === this.active());

    if (start < 0) {
      return;
    }

    for (let offset = 1; offset < tabs.length; offset += 1) {
      const index = (((start + delta * offset) % tabs.length) + tabs.length) % tabs.length;

      if (tabs[index].count > 0) {
        this.emitKeyboard(tabs[index].id);
        return;
      }
    }
  }

  private jumpTo(tab: ShowcaseTab | undefined): void {
    if (tab && tab.id !== this.active()) {
      this.emitKeyboard(tab.id);
    }
  }

  private enabledTabs(): ShowcaseTab[] {
    return this.tabs().filter((tab) => tab.count > 0);
  }

  private emitKeyboard(id: ShowcaseView): void {
    this.keyboardNavigated = true;
    this.activeChange.emit(id);
  }
}
