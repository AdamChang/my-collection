import { HttpClient } from '@angular/common/http';
import { Directive, ElementRef, OnDestroy, effect, inject, input, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { API_BASE } from '../core/api-base';

/**
 * 私有媒體透過 HttpClient 取得，讓 auth interceptor 附加 Bearer token。
 * 外部圖片與 share-scoped 公開網址直接交給瀏覽器，避免把 token 傳給第三方。
 */
@Directive({ selector: '[appAuthenticatedMedia]' })
export class AuthenticatedMediaDirective implements OnDestroy {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly http = inject(HttpClient, { optional: true });
  private request?: Subscription;
  private objectUrl?: string;

  readonly source = input.required<string>({ alias: 'appAuthenticatedMedia' });

  constructor() {
    // 只有 source 該讓這個 effect 重跑。load() 會同步啟動 HTTP，攔截器鏈在那段路徑上
    // 讀到的任何 signal（載入計數、access token）都會被登記成相依——一旦它們變動，
    // effect 重跑、取消原請求、再發一次，同一張圖就被無限重打。
    effect(() => {
      const source = this.source();
      untracked(() => this.load(source));
    });
  }

  ngOnDestroy(): void {
    this.clear();
  }

  private load(source: string): void {
    this.clear();
    this.element.dataset['mediaSource'] = source;

    if (!source.startsWith(`${API_BASE}/media/`)) {
      this.apply(source);
      return;
    }

    if (!this.http) {
      this.apply('');
      return;
    }

    this.request = this.http.get(source, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        this.objectUrl = URL.createObjectURL(blob);
        this.apply(this.objectUrl);
      },
      error: () => this.apply(''),
    });
  }

  private apply(source: string): void {
    if (this.element instanceof HTMLImageElement) {
      this.element.src = source;
    } else {
      this.element.style.backgroundImage = source ? `url("${source}")` : '';
    }
  }

  private clear(): void {
    this.request?.unsubscribe();
    this.request = undefined;

    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = undefined;
    }
  }
}
