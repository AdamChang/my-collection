import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './core/auth.service';
import { CatalogReturnPointService } from './core/catalog-return-point.service';
import { LoadingService } from './core/loading.service';
import { NotificationService } from './core/notification.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    @if (loading.showIndicator()) {
      <div class="progress" role="progressbar" aria-label="載入中" data-app-progress></div>
    }

    @if (auth.isAuthenticated() && !isPublicShell()) {
      <header class="app-header" data-app-shell>
        <a class="brand" routerLink="/" aria-label="MyCollection 首頁">
          <span class="brand__mark" aria-hidden="true"></span>
          <span>MY//COLLECTION</span>
        </a>
        <nav class="nav" aria-label="主要導覽">
          <div class="nav__links">
            <a routerLink="/" routerLinkActive="nav--active"
               [routerLinkActiveOptions]="{ exact: true }">精選</a>
            <a routerLink="/catalog" [queryParams]="returnPoint.queryParams()"
               routerLinkActive="nav--active">庫存</a>
            <a routerLink="/categories" routerLinkActive="nav--active">品類</a>
            <a routerLink="/settings" routerLinkActive="nav--active">設定</a>
          </div>
          <button type="button" class="nav__logout" (click)="auth.logout()">登出</button>
        </nav>
      </header>
    }

    <div class="toasts" aria-live="polite">
      @for (notification of notifications.notifications(); track notification.id) {
        <div class="toast" [class.toast--error]="notification.kind === 'error'">
          <span class="toast__status">{{ notification.kind === 'error' ? 'ERROR' : 'OK' }}</span>
          {{ notification.message }}
        </div>
      }
    </div>

    <main class="shell" [class.shell--public]="!auth.isAuthenticated() || isPublicShell()">
      <router-outlet />
    </main>
  `,
  styles: `
    :host { display: block; min-height: 100vh; }
    .progress { position: fixed; inset-block-start: 0; inset-inline: 0; z-index: 40; height: 2px;
      overflow: hidden; background: var(--mc-cyan-soft); }
    .progress::after { content: ''; display: block; width: 40%; height: 100%;
      background: linear-gradient(90deg, transparent, var(--mc-cyan), var(--mc-magenta), transparent);
      box-shadow: 0 0 12px var(--mc-cyan); animation: progress-sweep 1.1s linear infinite; }
    @keyframes progress-sweep {
      from { transform: translateX(-100%); }
      to { transform: translateX(350%); }
    }
    @media (prefers-reduced-motion: reduce) {
      .progress::after { width: 100%; animation: none; opacity: 0.7; }
    }
    .app-header { position: sticky; top: 0; z-index: 20; display: flex; align-items: center;
      justify-content: space-between; gap: 1rem; min-height: 4rem; padding: 0.65rem 1rem;
      border-bottom: 1px solid var(--mc-border); background: rgb(5 7 13 / 88%);
      backdrop-filter: blur(16px); }
    .brand { display: inline-flex; align-items: center; min-block-size: 44px; gap: 0.65rem; color: var(--mc-text);
      font: 800 0.84rem/1 Consolas, monospace; letter-spacing: 0.12em; text-decoration: none; }
    .brand__mark { display: inline-block; width: 1.2rem; height: 1.2rem; border: 2px solid var(--mc-cyan);
      transform: rotate(45deg); box-shadow: 0 0 14px var(--mc-cyan-soft); }
    .nav, .nav__links { display: flex; align-items: center; gap: 0.35rem; }
    .nav a { display: inline-flex; align-items: center; min-block-size: 44px;
      padding: 0.7rem 0.8rem; color: var(--mc-text-muted); text-decoration: none; }
    .nav a:hover, .nav a.nav--active { color: var(--mc-cyan); background: var(--mc-cyan-soft); }
    .nav__logout { margin-left: 0.5rem; }
    .shell { width: min(84rem, 100%); margin: 0 auto; padding: clamp(1rem, 3vw, 2rem); }
    .shell--public { width: 100%; padding: 0; }
    .toasts { position: fixed; top: 4.75rem; right: 1rem; z-index: 30; display: grid;
      gap: 0.5rem; width: min(24rem, calc(100vw - 2rem)); }
    .toast { display: grid; grid-template-columns: auto 1fr; gap: 0.65rem; padding: 0.8rem 1rem;
      border: 1px solid var(--mc-success); background: var(--mc-surface-raised); color: var(--mc-text); }
    .toast--error { border-color: var(--mc-danger); }
    .toast__status { color: var(--mc-success); font: 800 0.72rem/1.4 Consolas, monospace; }
    .toast--error .toast__status { color: var(--mc-danger); }
    @media (max-width: 700px) {
      .app-header { align-items: flex-start; flex-direction: column; }
      .nav { width: 100%; justify-content: space-between; }
      .nav__links { overflow-x: auto; }
    }
  `,
})
export class App {
  readonly auth = inject(AuthService);
  readonly loading = inject(LoadingService);
  readonly notifications = inject(NotificationService);

  /** 導覽列的「庫存」與品項頁的「返回列表」必須一致：一個記得、一個忘記比兩個都忘記更糟。 */
  readonly returnPoint = inject(CatalogReturnPointService);
  private readonly router = inject(Router);
  private readonly navigationEnd = toSignal(
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)),
    { initialValue: null },
  );
  readonly isPublicShell = computed(() => {
    this.navigationEnd();
    return this.router.routerState.snapshot.root.firstChild?.data['publicShell'] === true;
  });
}
