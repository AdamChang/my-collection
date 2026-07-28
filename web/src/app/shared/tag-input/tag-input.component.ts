import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-tag-input',
  template: `
    <div class="tags">
      @for (tag of tags(); track tag) {
        <span class="tags__chip">
          {{ tag }}
          <button type="button" (click)="removeTag(tag)" aria-label="移除">×</button>
        </span>
      }
      <input
        type="text"
        placeholder="新增標籤後按 Enter"
        (keydown.enter)="addTag($event)"
        (keydown.comma)="addTag($event)"
      />
    </div>
  `,
  styles: `
    .tags { display: flex; flex-wrap: wrap; gap: 0.4rem; align-items: center;
      border: 1px solid var(--mc-border); padding: 0.45rem; background: #07101a; }
    .tags__chip { display: inline-flex; align-items: center; gap: 0.25rem;
      border: 1px solid var(--mc-cyan); padding-left: 0.5rem; color: var(--mc-cyan); }
    .tags__chip button { min-width: 44px; min-height: 44px; border: 0; padding: 0; background: transparent; }
    .tags input { flex: 1; min-width: 9rem; border: 0 !important; outline: 0; }
  `,
})
export class TagInputComponent {
  readonly tags = input<string[]>([]);
  readonly tagsChange = output<string[]>();

  addTag(event: Event): void {
    event.preventDefault();
    const input = event.target as HTMLInputElement;
    const value = input.value.trim().replace(/,$/, '');

    if (value && !this.tags().includes(value)) {
      this.tagsChange.emit([...this.tags(), value]);
    }

    input.value = '';
  }

  removeTag(tag: string): void {
    this.tagsChange.emit(this.tags().filter((t) => t !== tag));
  }
}
