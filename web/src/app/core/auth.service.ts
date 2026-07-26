import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
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

  private readonly session = signal<StoredSession | null>(this.restore());

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

  /** 401 時由 auth.interceptor 呼叫。失敗代表 refresh token 也過期了。 */
  async refresh(): Promise<void> {
    const refreshToken = this.refreshToken();
    if (!refreshToken) {
      throw new Error('No refresh token available.');
    }

    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE}/auth/refresh`, { refreshToken }),
    );
    this.store(response);
  }

  logout(): void {
    this.session.set(null);
    localStorage.removeItem(STORAGE_KEY);
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
