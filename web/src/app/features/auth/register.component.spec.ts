import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { routes } from '../../app.routes';
import { AuthService } from '../../core/auth.service';
import { RegisterComponent } from './register.component';

describe('RegisterComponent', () => {
  let register: jasmine.Spy;

  async function setup(query: Record<string, string> = {}) {
    register = jasmine.createSpy('register').and.resolveTo();
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
        { provide: AuthService, useValue: { register } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    return fixture;
  }

  function fillForm(component: RegisterComponent): void {
    component.invitationCode = 'abcde-fghjk-mnpqr-stvwx';
    component.displayName = 'Adam';
    component.email = 'adam@example.com';
    component.password = 'P@ssw0rd!';
  }

  function alertOf(element: HTMLElement): HTMLElement | null {
    return element.querySelector('[role="alert"]');
  }

  it('prefills the invitation code from ?code=', async () => {
    const fixture = await setup({ code: 'ABCDE-FGHJK-MNPQR-STVWX' });

    expect(fixture.componentInstance.invitationCode).toBe('ABCDE-FGHJK-MNPQR-STVWX');
  });

  it('registers with the form values and goes to the showcase, ignoring returnUrl', async () => {
    const fixture = await setup({ returnUrl: '/catalog' });
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    fillForm(fixture.componentInstance);

    await fixture.componentInstance.submit();

    expect(register).toHaveBeenCalledOnceWith({
      email: 'adam@example.com',
      password: 'P@ssw0rd!',
      displayName: 'Adam',
      invitationCode: 'abcde-fghjk-mnpqr-stvwx',
    });
    expect(navigate).toHaveBeenCalledOnceWith('/');
  });

  it('explains a rejected invitation inline', async () => {
    const fixture = await setup();
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl');
    register.and.rejectWith(new HttpErrorResponse({ status: 403 }));
    fillForm(fixture.componentInstance);

    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(alertOf(fixture.nativeElement)?.textContent).toContain('邀請碼無效或已失效');
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.busy()).toBeFalse();
  });

  it('leaves other failures to the error toast', async () => {
    const fixture = await setup();
    register.and.rejectWith(new HttpErrorResponse({ status: 400 }));
    fillForm(fixture.componentInstance);

    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(alertOf(fixture.nativeElement)).toBeNull();
  });

  it('clears the rejection when submitting again', async () => {
    const fixture = await setup();
    spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    register.and.rejectWith(new HttpErrorResponse({ status: 403 }));
    fillForm(fixture.componentInstance);
    await fixture.componentInstance.submit();

    register.and.resolveTo();
    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(alertOf(fixture.nativeElement)).toBeNull();
  });

  it('keeps register actions at least 44px tall at 390px', async () => {
    const fixture = await setup();

    const frame = document.createElement('iframe');
    frame.style.width = '390px';
    frame.style.height = '844px';
    frame.style.border = '0';
    document.body.append(frame);

    try {
      const frameDocument = frame.contentDocument!;
      const styles = frameDocument.createElement('style');
      styles.textContent = Array.from(document.styleSheets)
        .flatMap((sheet) => Array.from(sheet.cssRules))
        .map((rule) => rule.cssText)
        .join('\n');
      frameDocument.head.append(styles);
      frameDocument.body.append(fixture.nativeElement.cloneNode(true));

      const targets = frameDocument.querySelectorAll<HTMLElement>(
        'button[type="submit"], .register__link',
      );
      expect(targets.length).toBe(2);
      targets.forEach((target) => {
        expect(parseFloat(frame.contentWindow!.getComputedStyle(target).height))
          .withContext(target.textContent?.trim() ?? target.tagName)
          .toBeGreaterThanOrEqual(44);
      });
    } finally {
      frame.remove();
    }
  });

  it('is routed at /register before the catch-all', async () => {
    const route = routes.find((r) => r.path === 'register');

    expect(route).toBeDefined();
    expect(await route!.loadComponent!()).toBe(RegisterComponent);
    expect(routes.indexOf(route!)).toBeLessThan(routes.findIndex((r) => r.path === '**'));
  });
});
