import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { TransferService } from '../../core/api/transfer.service';
import { NotificationService } from '../../core/notification.service';
import { ImageImportResultDto } from '../../core/models';
import { ImageTransferComponent } from './image-transfer.component';

describe('ImageTransferComponent', () => {
  const restored: ImageImportResultDto = { written: 3, skipped: 1, warnings: [] };

  const messages: string[] = [];
  let imported: File[] = [];

  // useValue 餵的是假服務，型別不必完全吻合真實簽章。
  async function create(transfer: unknown) {
    await TestBed.configureTestingModule({
      imports: [ImageTransferComponent],
      providers: [
        { provide: TransferService, useValue: transfer },
        { provide: NotificationService, useValue: { success: (m: string) => messages.push(m) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ImageTransferComponent);
    fixture.detectChanges();

    return fixture;
  }

  /** 回傳一個記錄呼叫的匯入用假服務。 */
  function importing(result: () => Observable<ImageImportResultDto>) {
    return {
      export: () => of(new Blob(['zip'])),
      import: (archive: File) => {
        imported.push(archive);
        return result();
      },
    };
  }

  function pick(fixture: ComponentFixture<ImageTransferComponent>, name = 'images.zip'): File {
    const file = new File(['zip'], name, { type: 'application/zip' });
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input[type=file]');
    const transfer = new DataTransfer();

    transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    return file;
  }

  beforeEach(() => {
    messages.length = 0;
    imported = [];
  });

  /**
   * 匯入只會補上缺的檔案，不覆蓋也不刪除，更不碰資料庫——它沒有破壞性，
   * 所以舊版那個「這會覆蓋這台機器的收藏」確認框已經拿掉。選檔後應該一鍵送出。
   */
  it('imports the picked archive without a second confirmation step', async () => {
    const fixture = await create(importing(() => of(restored)));
    const file = pick(fixture);

    fixture.nativeElement.querySelector('[data-import]').click();

    expect(imported).toEqual([file]);
    // 舊版的破壞性警告是一個 role="alertdialog" 的紅框，要再點一次「確定覆蓋」才會送出。
    expect(fixture.nativeElement.querySelector('[role=alertdialog]')).toBeNull();
  });

  it('reports how many files were written and how many were already here', async () => {
    const fixture = await create(importing(() => of(restored)));
    pick(fixture);

    fixture.nativeElement.querySelector('[data-import]').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('寫入 3');
    expect(fixture.nativeElement.textContent).toContain('略過 1');
    expect(messages).toEqual(['匯入完成']);
  });

  /**
   * 缺檔是匯出端當下就發現的事實，匯入端補不回來但必須說出來，
   * 否則使用者只會在某天看到破圖才知道圖早就掉了。
   */
  it('lists the files that were already missing when the archive was exported', async () => {
    const warnings = ['匯出來源缺少「Kind of Blue」的圖檔 o/i/img1-card.webp，這台機器上也不會有。'];
    const fixture = await create(importing(() => of({ written: 2, skipped: 0, warnings })));
    pick(fixture);

    fixture.nativeElement.querySelector('[data-import]').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Kind of Blue');
  });

  /** 失敗時保留已選檔案，使用者換一份封存檔或修好之後可以直接重試。 */
  it('keeps the picked file when the import fails', async () => {
    const fixture = await create(importing(() => throwError(() => new Error('boom'))));
    pick(fixture, 'broken.zip');

    fixture.nativeElement.querySelector('[data-import]').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('broken.zip');
    expect(fixture.nativeElement.querySelector('[data-import]')).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('匯入完成');
  });

  it('clears the previous result when another archive is picked', async () => {
    const fixture = await create(importing(() => of(restored)));
    pick(fixture);

    fixture.nativeElement.querySelector('[data-import]').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('寫入 3');

    pick(fixture, 'another.zip');

    expect(fixture.nativeElement.textContent).not.toContain('寫入 3');
  });

  it('locks both buttons while an import is in flight', async () => {
    const pending = new Subject<ImageImportResultDto>();
    const fixture = await create(importing(() => pending));
    pick(fixture);

    const importButton: HTMLButtonElement = fixture.nativeElement.querySelector('[data-import]');
    const exportButton: HTMLButtonElement = fixture.nativeElement.querySelector('[data-export]');

    importButton.click();
    fixture.detectChanges();

    expect(importButton.disabled).toBeTrue();
    expect(importButton.textContent).toContain('匯入中');
    expect(exportButton.disabled).toBeTrue();

    pending.next(restored);
    pending.complete();
    fixture.detectChanges();

    expect(exportButton.disabled).toBeFalse();
  });

  /** 這包只帶圖片，檔名要看得出來——別跟舊的資料封存檔混在同一個下載資料夾裡。 */
  it('downloads the export under an image-specific file name', async () => {
    const createElement = document.createElement.bind(document);
    const anchor = createElement('a');
    spyOn(anchor, 'click');
    spyOn(document, 'createElement').and.callFake((tag: string) =>
      tag === 'a' ? anchor : createElement(tag),
    );

    const fixture = await create({ export: () => of(new Blob(['zip'])), import: () => of(restored) });

    fixture.nativeElement.querySelector('[data-export]').click();

    expect(anchor.click).toHaveBeenCalled();
    expect(anchor.download).toMatch(/^mycollection-images-\d{4}-\d{2}-\d{2}\.zip$/);
  });
});
