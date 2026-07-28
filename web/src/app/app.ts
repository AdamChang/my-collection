import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './core/auth.service';
import { NotificationService } from './core/notification.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
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
            <a routerLink="/catalog" routerLinkActive="nav--active">庫存</a>
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
  readonly notifications = inject(NotificationService);
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
