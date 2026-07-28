import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  it('renders the authentication terminal and retains mode switching', async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        { provide: AuthService, useValue: { login: () => Promise.resolve(), register: () => Promise.resolve() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.login__terminal')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('input[name="displayName"]')).toBeNull();

    fixture.componentInstance.toggle();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input[name="displayName"]')).toBeTruthy();
  });
});
