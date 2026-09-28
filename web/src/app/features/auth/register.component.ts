import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink],
  template: `
    <main class="register">
      <section class="register__terminal mc-panel">
        <div class="mc-eyebrow">PRIVATE ARCHIVE / INVITATION</div>
        <h1>MY//COLLECTION</h1>
        <p class="mc-muted">註冊需要邀請碼。Email 必須與收到邀請的信箱相同。</p>
        <form (ngSubmit)="submit()">
          <label>邀請碼<input name="invitationCode" [(ngModel)]="invitationCode" required maxlength="64"
            autocomplete="off" autocapitalize="characters" spellcheck="false" /></label>
          <label>顯示名稱<input name="displayName" [(ngModel)]="displayName" required maxlength="64" /></label>
          <label>Email<input name="email" type="email" [(ngModel)]="email" required autocomplete="email" /></label>
          <label>密碼<input name="password" type="password" [(ngModel)]="password" required minlength="8"
            autocomplete="new-password" /></label>

          @if (rejected()) {
            <p class="register__alert" role="alert">
              邀請碼無效或已失效。請確認 email 與收到邀請的信箱相同；邀請只能使用一次，且有使用期限。
              仍無法註冊時，請向邀請你的人索取新的邀請。
            </p>
          }

          <button type="submit" [disabled]="busy()">{{ busy() ? '連線中…' : '建立帳號' }}</button>
        </form>

        <a class="register__link" routerLink="/login">已經有帳號？登入</a>
      </section>
    </main>
  `,
  // 與 LoginComponent 相似但刻意不抽共用：目前只有兩處，出現第三個類似頁面再抽
  styles: `
    .register { min-height: calc(100vh - 8rem); display: grid; place-items: center; padding: 2rem 1rem; }
    .register__terminal { width: min(100%, 26rem); display: grid; gap: 1rem; }
    .register h1 { margin: 0; letter-spacing: 0.08em; }
    .register p { margin: 0; }
    .register form { display: grid; gap: 0.75rem; }
    .register label { display: grid; gap: 0.35rem; color: var(--mc-text-muted); }
    .register__alert { color: var(--mc-magenta); }
    .register__link { display: inline-flex; align-items: center; justify-self: start;
      min-block-size: 44px; color: var(--mc-cyan); }
  `,
})
export class RegisterComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  /** 後端對所有無效原因回同一個 403（ADR-0013），這裡也只給通用說明。 */
  readonly rejected = signal(false);

  /** 維運指令產生的連結會帶 ?code=。只當初值，之後以欄位為準（見設計文件決策二）。 */
  invitationCode = inject(ActivatedRoute).snapshot.queryParamMap.get('code') ?? '';
  displayName = '';
  email = '';
  password = '';

  async submit(): Promise<void> {
    this.busy.set(true);
    this.rejected.set(false);

    try {
      await this.auth.register({
        email: this.email,
        password: this.password,
        displayName: this.displayName,
        invitationCode: this.invitationCode,
      });

      // 不讀 returnUrl：新帳號沒有「登入前想去的頁面」，也避免註冊連結被做成開放轉址
      await this.router.navigateByUrl('/');
    } catch (error) {
      // 403 的 toast 已由 AuthService 關掉，改在頁內說明；其他錯誤 errorInterceptor 已經顯示
      if (error instanceof HttpErrorResponse && error.status === 403) {
        this.rejected.set(true);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
