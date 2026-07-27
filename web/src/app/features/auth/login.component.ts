import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <main class="login">
      <h1>MyCollection</h1>

      <form (ngSubmit)="submit()">
        @if (mode() === 'register') {
          <label>顯示名稱<input name="displayName" [(ngModel)]="displayName" required /></label>
        }
        <label>Email<input name="email" type="email" [(ngModel)]="email" required /></label>
        <label>密碼<input name="password" type="password" [(ngModel)]="password" required minlength="8" /></label>

        <button type="submit" [disabled]="busy()">
          {{ mode() === 'login' ? '登入' : '註冊' }}
        </button>
      </form>

      <button type="button" class="login__toggle" (click)="toggle()">
        {{ mode() === 'login' ? '還沒有帳號？註冊' : '已經有帳號？登入' }}
      </button>
    </main>
  `,
  styles: `
    .login { max-width: 22rem; margin: 4rem auto; display: grid; gap: 1rem; }
    .login form { display: grid; gap: 0.75rem; }
    .login label { display: grid; gap: 0.25rem; }
    .login__toggle { background: none; border: 0; color: #2980b9; cursor: pointer; }
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
