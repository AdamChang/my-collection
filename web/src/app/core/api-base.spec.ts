import { resolveApiBase } from './api-base';

describe('resolveApiBase', () => {
  it('falls back to the local reverse-proxy path', () => {
    expect(resolveApiBase()).toBe('/api');
  });

  it('uses a runtime Cloud Run API URL without a trailing slash', () => {
    expect(resolveApiBase(' https://api.example.run.app/ ')).toBe(
      'https://api.example.run.app',
    );
  });
});
