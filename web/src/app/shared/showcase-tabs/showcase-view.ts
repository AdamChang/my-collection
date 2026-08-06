/** 精選頁的四個展示頁籤。順序即畫面上的排列順序，拼貼牆是預設。 */
export type ShowcaseView = 'collage' | 'hero' | 'stats' | 'list';

export const SHOWCASE_VIEWS: readonly ShowcaseView[] = ['collage', 'hero', 'stats', 'list'];

export const DEFAULT_SHOWCASE_VIEW: ShowcaseView = 'collage';

/**
 * 把 query param 正規化成合法頁籤。`?view=` 是使用者可以隨手亂打的東西，
 * 無效值一律退回拼貼牆，不能讓四個分區同時消失變成空白頁。
 */
export function parseShowcaseView(value: string | null | undefined): ShowcaseView {
  return SHOWCASE_VIEWS.includes(value as ShowcaseView)
    ? (value as ShowcaseView)
    : DEFAULT_SHOWCASE_VIEW;
}
