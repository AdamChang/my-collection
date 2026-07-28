import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE } from './api-base';
import { AuthResponse, UserDto } from './models';

const STORAGE_KEY = 'mycollection.session';

interface StoredSession {
  accessToken: string;
  refreshToken: string;
  user: UserDto;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<StoredSession | null>(this.restore());

  /** 進行中的換發。見 refresh() 的說明。 */
  private inFlightRefresh: Promise<void> | null = null;

  readonly accessToken = computed(() => this.session()?.accessToken ?? null);
  readonly refreshToken = computed(() => this.session()?.refreshToken ?? null);
  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);

  async register(email: string, password: string, displayName: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE}/auth/register`, { email, password, displayName }),
    );
    this.store(response);
  }

  async login(email: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE}/auth/login`, { email, password }),
    );
    this.store(response);
  }

  /**
   * 401 時由 auth.interceptor 呼叫。失敗代表 refresh token 也過期或已被作廢。
   *
   * 後端的 refresh token 是 rotation 的（換發成功即作廢舊 token），而每個頁面初始化
   * 都平行送 2-3 個請求。若讓它們各自換發，第一個會轉走 token，其餘拿著已作廢的
   * token 得到 403，反而把剛換好的 session 清掉。因此同一時間只允許一次換發在飛，
   * 後到的呼叫者共用同一個 promise；結算後清空快取，下一輪過期才能再換一次。
   */
  refresh(): Promise<void> {
    this.inFlightRefresh ??= this.requestRefresh().finally(() => {
      this.inFlightRefresh = null;
    });

    return this.inFlightRefresh;
  }

  /**
   * @param returnUrl session 失效時傳入當下位置，讓使用者重新登入後回到原頁；
   * 使用者主動登出則不傳。
   */
  logout(returnUrl?: string): void {
    this.session.set(null);
    this.inFlightRefresh = null;
    localStorage.removeItem(STORAGE_KEY);

    void this.router.navigate(['/login'], returnUrl ? { queryParams: { returnUrl } } : {});
  }

  private async requestRefresh(): Promise<void> {
    const refreshToken = this.refreshToken();
    if (!refreshToken) {
      throw new Error('No refresh token available.');
    }

    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE}/auth/refresh`, { refreshToken }),
    );
    this.store(response);
  }

  private store(response: AuthResponse): void {
    const session: StoredSession = {
      accessToken: response.accessToken,
      refreshToken: response.refreshToken,
      user: response.user,
    };
    this.session.set(session);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  }

  private restore(): StoredSession | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as StoredSession;
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}
