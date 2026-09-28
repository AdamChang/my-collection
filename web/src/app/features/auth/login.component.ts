import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink],
  template: `
    <main class="login">
      <section class="login__terminal mc-panel">
        <div class="mc-eyebrow">PRIVATE ARCHIVE / AUTH GATE</div>
        <h1>MY//COLLECTION</h1>
        <p class="mc-muted">跨越實體與數位世界，建立你的私人收藏座標。</p>
        <form (ngSubmit)="submit()">
          <label>Email<input name="email" type="email" [(ngModel)]="email" required /></label>
          <label>密碼<input name="password" type="password" [(ngModel)]="password" required minlength="8" /></label>

          <button type="submit" [disabled]="busy()">
            {{ busy() ? '連線中…' : '登入系統' }}
          </button>
        </form>

        <!-- 註冊採邀請制（ADR-0013），被邀請者可能只拿到碼、沒拿到連結，所以保留入口 -->
        <a class="login__link" routerLink="/register">有邀請碼？前往註冊</a>
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
    .login__link { display: inline-flex; align-items: center; justify-self: start;
      min-block-size: 44px; color: var(--mc-cyan); }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly busy = signal(false);

  email = '';
  password = '';

  async submit(): Promise<void> {
    this.busy.set(true);

    try {
      await this.auth.login(this.email, this.password);

      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
      await this.router.navigateByUrl(returnUrl);
    } catch {
      // errorInterceptor 已經顯示訊息
    } finally {
      this.busy.set(false);
    }
  }
}
