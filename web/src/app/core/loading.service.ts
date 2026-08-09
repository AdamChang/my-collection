import { Injectable, computed, signal, untracked } from '@angular/core';

/** 短請求不值得閃一下進度條，那比不顯示更像壞掉。 */
const INDICATOR_DELAY_MS = 200;

/**
 * 全站 in-flight 請求計數。由 loading.interceptor 驅動，讓每個頁面不必各自實作載入狀態。
 */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly pending = signal(0);
  private timer: ReturnType<typeof setTimeout> | null = null;

  /** 是否有請求在飛。立即反應，不含延遲。 */
  readonly isBusy = computed(() => this.pending() > 0);

  /** 進度條是否該顯示。忙碌超過 INDICATOR_DELAY_MS 才為 true。 */
  readonly showIndicator = signal(false);

  /**
   * untracked 不是最佳化：請求常在 effect 裡發出，若計數器在這裡被反應式讀取，
   * 呼叫端的 effect 就會相依於它，而下一行的寫入立刻讓那個 effect 變髒——
   * 重跑、取消原請求、再發一次，無限迴圈。計數的讀取永遠不該建立相依。
   */
  start(): void {
    const pending = untracked(this.pending) + 1;
    this.pending.set(pending);

    if (pending === 1) {
      this.timer = setTimeout(() => this.showIndicator.set(true), INDICATOR_DELAY_MS);
    }
  }

  stop(): void {
    // 夾在 0 以上：多餘的 stop() 若讓計數變負，之後的進度條就再也不會消失。
    const pending = Math.max(0, untracked(this.pending) - 1);
    this.pending.set(pending);

    if (pending === 0) {
      this.cancelTimer();
      this.showIndicator.set(false);
    }
  }

  private cancelTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
