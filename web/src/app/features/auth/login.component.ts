import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <main class="login">
      <section class="login__terminal mc-panel">
        <div class="mc-eyebrow">PRIVATE ARCHIVE / AUTH GATE</div>
        <h1>MY//COLLECTION</h1>
        <p class="mc-muted">跨越實體與數位世界，建立你的私人收藏座標。</p>
        <form (ngSubmit)="submit()">
          @if (mode() === 'register') {
            <label>顯示名稱<input name="displayName" [(ngModel)]="displayName" required /></label>
          }
          <label>Email<input name="email" type="email" [(ngModel)]="email" required /></label>
          <label>密碼<input name="password" type="password" [(ngModel)]="password" required minlength="8" /></label>

          <button type="submit" [disabled]="busy()">
            {{ busy() ? '連線中…' : mode() === 'login' ? '登入系統' : '建立帳號' }}
          </button>
        </form>

        <button type="button" class="login__toggle" (click)="toggle()">
          {{ mode() === 'login' ? '還沒有帳號？註冊' : '已經有帳號？登入' }}
        </button>
      </section>
    </main>
  `,
  styles: `
    .login { min-height: calc(100vh - 8rem); display: grid; place-items: center; padding: 2rem 1rem; }
    .login__terminal { width: min(100%, 26rem); display: grid; gap: 1rem; }
    .login h1 { margin: 0; letter-spacing: 0.08em; }
    .login p { margin: 0; }
    .login form { display: grid; gap: 0.75rem; }
    .login label { display: grid; gap: 0.35rem; color: var(--mc-text-muted); }
    .login__toggle { justify-self: start; min-height: auto; border: 0; padding: 0; background: none; color: var(--mc-cyan); }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly mode = signal<'login' | 'register'>('login');
  readonly busy = signal(false);

  email = '';
  password = '';
  displayName = '';

  toggle(): void {
    this.mode.update((m) => (m === 'login' ? 'register' : 'login'));
  }

  async submit(): Promise<void> {
    this.busy.set(true);

    try {
      if (this.mode() === 'login') {
        await this.auth.login(this.email, this.password);
      } else {
        await this.auth.register(this.email, this.password, this.displayName);
      }

      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
      await this.router.navigateByUrl(returnUrl);
    } catch {
      // errorInterceptor 已經顯示訊息
    } finally {
      this.busy.set(false);
    }
  }
}
