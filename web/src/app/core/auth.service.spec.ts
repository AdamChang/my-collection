import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';
import { AuthResponse } from './models';

const response: AuthResponse = {
  accessToken: 'access-1',
  refreshToken: 'refresh-1',
  expiresAt: '2026-07-25T03:30:00Z',
  user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
};

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts unauthenticated', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
  });

  it('stores tokens and user after login', async () => {
    const promise = service.login('a@b.c', 'P@ssw0rd!');
    http.expectOne(`/api/auth/login`).flush(response);
    await promise;

    expect(service.isAuthenticated()).toBe(true);
    expect(service.accessToken()).toBe('access-1');
    expect(service.user()?.displayName).toBe('Adam');
  });

  it('restores the session from storage on construction', async () => {
    const promise = service.login('a@b.c', 'P@ssw0rd!');
    http.expectOne(`/api/auth/login`).flush(response);
    await promise;

    const restored = TestBed.runInInjectionContext(() => new AuthService());
    expect(restored.isAuthenticated()).toBe(true);
    expect(restored.accessToken()).toBe('access-1');
  });

  it('clears everything on logout', async () => {
    const promise = service.login('a@b.c', 'P@ssw0rd!');
    http.expectOne(`/api/auth/login`).flush(response);
    await promise;

    service.logout();

    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('mycollection.session')).toBeNull();
  });

  it('refresh replaces the stored token pair', async () => {
    const login = service.login('a@b.c', 'P@ssw0rd!');
    http.expectOne(`/api/auth/login`).flush(response);
    await login;

    const refresh = service.refresh();
    const request = http.expectOne(`/api/auth/refresh`);
    expect(request.request.body).toEqual({ refreshToken: 'refresh-1' });
    request.flush({ ...response, accessToken: 'access-2', refreshToken: 'refresh-2' });
    await refresh;

    expect(service.accessToken()).toBe('access-2');
  });
});
