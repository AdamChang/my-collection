import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CategoryFieldDto } from '../../core/models';
import { DynamicFormComponent } from './dynamic-form.component';

function field(overrides: Partial<CategoryFieldDto>): CategoryFieldDto {
  return {
    key: 'brand',
    label: '廠商',
    type: 'Text',
    options: null,
    required: false,
    searchable: false,
    showOnCard: false,
    ...overrides,
  };
}

describe('DynamicFormComponent', () => {
  let fixture: ComponentFixture<DynamicFormComponent>;
  let component: DynamicFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DynamicFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(DynamicFormComponent);
    component = fixture.componentInstance;
  });

  function render(fields: CategoryFieldDto[], value: Record<string, unknown> = {}): void {
    fixture.componentRef.setInput('fields', fields);
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
  }

  it('renders one control per field', () => {
    render([field({ key: 'brand' }), field({ key: 'scale', label: '比例' })]);

    const inputs = fixture.nativeElement.querySelectorAll('[data-field]');
    expect(inputs.length).toBe(2);
  });

  it('renders a select with the schema options', () => {
    render([field({ key: 'brand', type: 'Select', options: ['GSC', 'ALTER'] })]);

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select[data-field="brand"]');
    expect(select).toBeTruthy();
    expect(Array.from(select.options).map((o) => o.value)).toEqual(['', 'GSC', 'ALTER']);
  });

  it('maps field types to input types', () => {
    render([
      field({ key: 'height', type: 'Number' }),
      field({ key: 'releasedAt', type: 'Date' }),
      field({ key: 'isLimited', type: 'Bool' }),
      field({ key: 'productUrl', type: 'Url' }),
    ]);

    const el = fixture.nativeElement;
    expect(el.querySelector('[data-field="height"]').type).toBe('number');
    expect(el.querySelector('[data-field="releasedAt"]').type).toBe('date');
    expect(el.querySelector('[data-field="isLimited"]').type).toBe('checkbox');
    expect(el.querySelector('[data-field="productUrl"]').type).toBe('url');
  });

  it('marks required fields invalid when empty', () => {
    render([field({ key: 'brand', required: true })]);

    expect(component.form.valid).toBe(false);

    component.form.controls['brand'].setValue('GSC');
    expect(component.form.valid).toBe(true);
  });

  it('validates url fields', () => {
    render([field({ key: 'productUrl', type: 'Url' })]);

    component.form.controls['productUrl'].setValue('not a url');
    expect(component.form.valid).toBe(false);

    component.form.controls['productUrl'].setValue('https://example.com/a');
    expect(component.form.valid).toBe(true);
  });

  it('patches initial values from the value input', () => {
    render([field({ key: 'brand' }), field({ key: 'height', type: 'Number' })], {
      brand: 'GSC',
      height: 200,
    });

    expect(component.form.value).toEqual({ brand: 'GSC', height: 200 });
  });

  it('emits attributes with empty strings dropped', () => {
    const emitted: Record<string, unknown>[] = [];
    render([field({ key: 'brand' }), field({ key: 'scale' })], { brand: 'GSC' });
    component.valueChange.subscribe((v) => emitted.push(v));

    component.form.controls['brand'].setValue('ALTER');

    expect(emitted.at(-1)).toEqual({ brand: 'ALTER' });
  });

  it('coerces date values to ISO-8601 UTC', () => {
    const emitted: Record<string, unknown>[] = [];
    render([field({ key: 'releasedAt', type: 'Date' })]);
    component.valueChange.subscribe((v) => emitted.push(v));

    component.form.controls['releasedAt'].setValue('2026-01-15');

    expect(emitted.at(-1)).toEqual({ releasedAt: '2026-01-15T00:00:00.000Z' });
  });

  it('rebuilds the form when the schema changes', () => {
    render([field({ key: 'brand' })]);
    expect(Object.keys(component.form.controls)).toEqual(['brand']);

    fixture.componentRef.setInput('fields', [field({ key: 'publisher', label: '發行商' })]);
    fixture.detectChanges();

    expect(Object.keys(component.form.controls)).toEqual(['publisher']);
  });
});
