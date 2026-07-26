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
    .tags { display: flex; flex-wrap: wrap; gap: 0.35rem; align-items: center;
            border: 1px solid #dfe4e6; border-radius: 0.5rem; padding: 0.35rem; }
    .tags__chip { display: inline-flex; gap: 0.25rem; align-items: center; font-size: 0.8rem;
                  background: #ecf0f1; border-radius: 0.35rem; padding: 0.1rem 0.4rem; }
    .tags input { border: 0; outline: none; flex: 1; min-width: 8rem; }
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
