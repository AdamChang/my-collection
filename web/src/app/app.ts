import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { NotificationService } from './core/notification.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    @if (auth.isAuthenticated()) {
      <nav class="nav">
        <a routerLink="/" routerLinkActive="nav--active" [routerLinkActiveOptions]="{ exact: true }">精選</a>
        <a routerLink="/catalog" routerLinkActive="nav--active">庫存</a>
        <a routerLink="/categories" routerLinkActive="nav--active">品類</a>
        <a routerLink="/settings" routerLinkActive="nav--active">設定</a>
        <button type="button" (click)="auth.logout()">登出</button>
      </nav>
    }

    <div class="toasts">
      @for (notification of notifications.notifications(); track notification.id) {
        <div class="toast" [class.toast--error]="notification.kind === 'error'">
          {{ notification.message }}
        </div>
      }
    </div>

    <main class="shell">
      <router-outlet />
    </main>
  `,
  styles: `
    .nav { display: flex; gap: 1rem; align-items: center; padding: 0.75rem 1rem; border-bottom: 1px solid #ecf0f1; }
    .nav--active { font-weight: 600; }
    .shell { max-width: 72rem; margin: 1.5rem auto; padding: 0 1rem; }
    .toasts { position: fixed; top: 1rem; right: 1rem; display: grid; gap: 0.5rem; z-index: 10; }
    .toast { padding: 0.6rem 0.9rem; border-radius: 0.5rem; background: #2ecc71; color: #fff; max-width: 22rem; white-space: pre-line; }
    .toast--error { background: #e74c3c; }
  `,
})
export class App {
  readonly auth = inject(AuthService);
  readonly notifications = inject(NotificationService);
}
