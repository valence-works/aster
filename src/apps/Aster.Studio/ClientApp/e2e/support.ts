import { expect, type Page } from "@playwright/test";

// Shared by the smoke specs: they run against the real host and a freshly seeded SQLite database.

export const field = (page: Page, aspectKey: string, facetId: string) => page.locator(`[id="${aspectKey}.${facetId}"]`);
export const unique = (prefix: string) => `${prefix}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

/** Creates an article through the editor and returns its resource ID. */
export async function createArticle(page: Page, title: string) {
  await page.goto("/content/article/new");
  await field(page, "content", "title").fill(title);
  await page.getByRole("button", { name: "Title", exact: true }).click(); // Generate the slug from the title.
  await page.getByRole("button", { name: "Create resource" }).click();
  await expect(page).toHaveURL(/\/content\/article\/(?!new$)[^/]+$/);
  await expect(page.getByText("base v1")).toBeVisible();
  return decodeURIComponent(new URL(page.url()).pathname.split("/").pop()!);
}

export async function saveNewVersion(page: Page, title: string, expectedVersion: number) {
  await field(page, "content", "title").fill(title);
  await page.getByRole("button", { name: "Save new version" }).click();
  await expect(page.getByText(`base v${expectedVersion}`)).toBeVisible();
}

export async function setChannel(page: Page, version: number, action: "Activate" | "Deactivate", channel: "preview" | "live") {
  const row = versionRow(page, version);
  await row.getByRole("button", { name: `${action} ${channel}` }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: `${action} v${version} ${channel}` })).toBeVisible();
  await dialog.getByRole("button", { name: "Confirm" }).click();
  await expect(dialog).toBeHidden();
}

export const versionRow = (page: Page, version: number) =>
  page.getByRole("tabpanel", { name: "Timeline" }).locator(":scope > div").filter({ has: page.getByText(`v${version}`, { exact: true }) });
