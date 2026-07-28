import { Component, effect, input, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { CategoryFieldDto } from '../../core/models';

/**
 * 吃 CategoryField[] 產出 Reactive Form。這是 schema 驅動的最後一哩：
 * 同一份 fields 已經產生了後端驗證規則與篩選器，這裡再產生表單。
 */
@Component({
  selector: 'app-dynamic-form',
  imports: [ReactiveFormsModule],
  template: `
    <div [formGroup]="form" class="dynamic-form" data-schema-fields>
      @for (field of fields(); track field.key) {
        <label class="dynamic-form__row">
          <span class="dynamic-form__label">
            {{ field.label }}
            @if (field.required) { <em aria-hidden="true">*</em> }
          </span>

          @switch (field.type) {
            @case ('Select') {
              <select [formControlName]="field.key" [attr.data-field]="field.key">
                <option value=""></option>
                @for (option of field.options ?? []; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
            }
            @case ('Bool') {
              <input type="checkbox" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
            @case ('Number') {
              <input type="number" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
            @case ('Date') {
              <input type="date" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
            @case ('Url') {
              <input type="url" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
            @default {
              <input type="text" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
          }

          @if (form.controls[field.key]?.invalid && form.controls[field.key]?.touched) {
            <small class="dynamic-form__error">{{ errorFor(field) }}</small>
          }
        </label>
      }
    </div>
  `,
  styles: `
    .dynamic-form { display: grid; gap: 0.75rem; }
    .dynamic-form__row { display: grid; gap: 0.25rem; }
    .dynamic-form__label em { color: var(--mc-warning); font-style: normal; }
    .dynamic-form__error { color: var(--mc-warning); font-size: 0.8rem; }
  `,
})
export class DynamicFormComponent {
  readonly fields = input.required<CategoryFieldDto[]>();
  readonly value = input<Record<string, unknown>>({});

  readonly valueChange = output<Record<string, unknown>>();
  readonly validityChange = output<boolean>();

  form = new FormGroup<Record<string, FormControl>>({});

  constructor() {
    effect(() => {
      const fields = this.fields();
      const initial = this.value();

      this.form = new FormGroup<Record<string, FormControl>>(
        Object.fromEntries(fields.map((f) => [f.key, this.buildControl(f, initial[f.key])])),
      );

      this.form.valueChanges.subscribe(() => {
        this.valueChange.emit(this.attributes());
        this.validityChange.emit(this.form.valid);
      });

      this.validityChange.emit(this.form.valid);
    });
  }

  /** 目前表單值，已剔除空值——後端把空字串當成型別錯誤。 */
  attributes(): Record<string, unknown> {
    const result: Record<string, unknown> = {};

    for (const field of this.fields()) {
      const raw = this.form.controls[field.key]?.value;

      if (raw === null || raw === undefined || raw === '') {
        continue;
      }

      result[field.key] = this.coerce(field, raw);
    }

    return result;
  }

  errorFor(field: CategoryFieldDto): string {
    const control = this.form.controls[field.key];

    if (control?.hasError('required')) {
      return `${field.label} 為必填`;
    }
    if (control?.hasError('pattern')) {
      return `${field.label} 必須是完整的 http(s) 網址`;
    }

    return `${field.label} 格式不正確`;
  }

  private buildControl(field: CategoryFieldDto, initial: unknown): FormControl {
    const validators: ValidatorFn[] = [];

    if (field.required) {
      validators.push(field.type === 'Bool' ? Validators.requiredTrue : Validators.required);
    }

    if (field.type === 'Url') {
      validators.push(Validators.pattern(/^https?:\/\/\S+$/));
    }

    return new FormControl(this.toControlValue(field, initial), validators);
  }

  private toControlValue(field: CategoryFieldDto, initial: unknown): unknown {
    if (initial === null || initial === undefined) {
      return field.type === 'Bool' ? false : '';
    }

    // ISO-8601 → yyyy-MM-dd，input[type=date] 只接受這個格式
    if (field.type === 'Date' && typeof initial === 'string') {
      return initial.slice(0, 10);
    }

    return initial;
  }

  private coerce(field: CategoryFieldDto, raw: unknown): unknown {
    switch (field.type) {
      case 'Number':
        return Number(raw);
      case 'Bool':
        return Boolean(raw);
      case 'Date':
        return new Date(`${String(raw)}T00:00:00Z`).toISOString();
      default:
        return raw;
    }
  }
}
