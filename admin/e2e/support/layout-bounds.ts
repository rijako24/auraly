import { expect, type Locator } from "@playwright/test";

/** Keep failing layout checks actionable without weakening viewport bounds. */
export async function expectNoHorizontalOverflow(root: Locator) {
  const layout = await root.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return {
      width: element.clientWidth,
      scrollWidth: element.scrollWidth,
      viewport: innerWidth,
      offenders: [...element.querySelectorAll("*")].map(child => {
        const rect = child.getBoundingClientRect();
        return { tag: child.tagName, className: child.getAttribute("class"),
          text: child.textContent?.slice(0, 100), left: rect.left, right: rect.right, width: rect.width };
      }).filter(child => child.width > 0 && (child.left < bounds.left - 1 || child.right > bounds.right + 1)).slice(0, 12),
    };
  });
  expect(layout.scrollWidth, JSON.stringify(layout)).toBeLessThanOrEqual(layout.width);
}
