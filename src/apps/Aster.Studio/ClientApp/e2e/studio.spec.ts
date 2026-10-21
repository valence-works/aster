import { expect, test, type Page } from "@playwright/test";

// Smoke coverage for Studio's core flows against the real host and a freshly seeded SQLite database.
// Each test creates the data it changes, so the tests don't depend on one another's order or leftovers.

const field = (page: Page, aspectKey: string, facetId: string) => page.locator(`[id="${aspectKey}.${facetId}"]`);
const unique = (prefix: string) => `${prefix}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

async function createArticle(page: Page, title: string) {
  await page.goto("/content/article/new");
  await field(page, "content", "title").fill(title);
  await page.getByRole("button", { name: "Title", exact: true }).click(); // Generate the slug from the title.
  await page.getByRole("button", { name: "Create resource" }).click();
  await expect(page).toHaveURL(/\/content\/article\/(?!new$)[^/]+$/);
  await expect(page.getByText("base v1")).toBeVisible();
}

async function saveNewVersion(page: Page, title: string, expectedVersion: number) {
  await field(page, "content", "title").fill(title);
  await page.getByRole("button", { name: "Save new version" }).click();
  await expect(page.getByText(`base v${expectedVersion}`)).toBeVisible();
}

async function setChannel(page: Page, version: number, action: "Activate" | "Deactivate", channel: "preview" | "live") {
  const row = versionRow(page, version);
  await row.getByRole("button", { name: `${action} ${channel}` }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: `${action} v${version} ${channel}` })).toBeVisible();
  await dialog.getByRole("button", { name: "Confirm" }).click();
  await expect(dialog).toBeHidden();
}

const versionRow = (page: Page, version: number) =>
  page.getByRole("tabpanel", { name: "Timeline" }).locator(":scope > div").filter({ has: page.getByText(`v${version}`, { exact: true }) });

test("creates a content type from shared aspects and edits it with the generated form", async ({ page }) => {
  const definitionId = unique("smoke-type");

  await page.goto("/content-types");
  await page.getByRole("button", { name: "New content type" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Definition id").fill(definitionId);
  await dialog.getByRole("button", { name: /\bcontent · v\d+/ }).click();
  await dialog.getByRole("button", { name: /\btaggable · v\d+/ }).click();
  await dialog.getByRole("button", { name: "Create definition" }).click();
  await expect(dialog).toBeHidden();

  const row = page.getByRole("row").filter({ hasText: definitionId });
  await expect(row).toBeVisible();
  await expect(row.getByRole("cell").nth(2)).toHaveText("2");

  // The new type is immediately editable: its form is generated from the pinned aspects.
  await page.goto(`/content/${definitionId}/new`);
  await expect(field(page, "content", "title")).toBeVisible();
  await expect(field(page, "taggable", "tags")).toBeVisible();
  await expect(field(page, "seo", "slug")).toHaveCount(0);
  await field(page, "content", "title").fill("Made by the smoke suite");
  await page.getByRole("button", { name: "Create resource" }).click();
  await expect(page).toHaveURL(new RegExp(`/content/${definitionId}/(?!new$)[^/]+$`));
  await expect(page.getByText("base v1")).toBeVisible();
});

test("creates, edits and validates a resource with field-level diagnostics", async ({ page }) => {
  // Client-side validation mirrors facet metadata: required fields are flagged before any request is sent.
  await page.goto("/content/article/new");
  await page.getByRole("button", { name: "Create resource" }).click();
  await expect(page.getByText("Title is required.")).toBeVisible();
  await expect(page).toHaveURL(/\/content\/article\/new$/);

  // Server-side validation: send a title the client would reject, and the server's diagnostic lands on that field.
  await field(page, "content", "title").fill("Valid title");
  await page.getByRole("button", { name: "Title", exact: true }).click();
  await page.route("**/api/aster/resources", async (route) => {
    const body = route.request().postDataJSON();
    body.aspects.content.title = "x".repeat(201);
    await route.continue({ postData: JSON.stringify(body) });
  }, { times: 1 });
  await page.getByRole("button", { name: "Create resource" }).click();
  await expect(page.getByText("Value length (201) is outside the declared length bounds.")).toBeVisible();
  await expect(page).toHaveURL(/\/content\/article\/new$/);

  // A valid create, then an edit that appends an immutable version.
  const title = unique("Smoke article");
  await field(page, "content", "title").fill(title);
  await page.getByRole("button", { name: "Create resource" }).click();
  await expect(page).toHaveURL(/\/content\/article\/(?!new$)[^/]+$/);
  await expect(page.getByText("base v1")).toBeVisible();
  await expect(field(page, "content", "title")).toHaveValue(title);

  await saveNewVersion(page, `${title} (edited)`, 2);
  await page.reload();
  await expect(field(page, "content", "title")).toHaveValue(`${title} (edited)`);
});

test("activates and deactivates a version on preview and live", async ({ page }) => {
  await createArticle(page, unique("Channel article"));

  await setChannel(page, 1, "Activate", "preview");
  await setChannel(page, 1, "Activate", "live");
  await expect(versionRow(page, 1).getByRole("button", { name: "Deactivate preview" })).toBeVisible();
  await expect(versionRow(page, 1).getByRole("button", { name: "Deactivate live" })).toBeVisible();

  await page.getByRole("tab", { name: "Channels" }).click();
  const channels = page.getByRole("tabpanel");
  await expect(channels.getByText("No version active on preview.")).toHaveCount(0);
  await expect(channels.getByText("No version active on live.")).toHaveCount(0);

  await page.getByRole("tab", { name: "Timeline" }).click();
  await setChannel(page, 1, "Deactivate", "preview");
  await expect(versionRow(page, 1).getByRole("button", { name: "Activate preview" })).toBeVisible();
  await expect(versionRow(page, 1).getByRole("button", { name: "Deactivate live" })).toBeVisible();

  await page.getByRole("tab", { name: "Channels" }).click();
  await expect(page.getByRole("tabpanel").getByText("No version active on preview.")).toBeVisible();
});

test("keeps every version in the history and renders an older one read-only", async ({ page }) => {
  const title = unique("History article");
  await createArticle(page, title);
  await saveNewVersion(page, `${title} v2`, 2);
  await saveNewVersion(page, `${title} v3`, 3);

  await expect(versionRow(page, 3).getByText("latest")).toBeVisible();
  await expect(versionRow(page, 1).getByText("latest")).toHaveCount(0);

  await versionRow(page, 1).getByRole("button", { name: "View", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: "Viewing v1" })).toBeVisible();
  await expect(dialog.locator('[id="content.title"]')).toHaveValue(title);
  await expect(dialog.locator('[id="content.title"]')).toBeDisabled();
  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();

  // Viewing history doesn't change what the editor is based on.
  await expect(page.getByText("base v3")).toBeVisible();
});

test("filters the content list by tag", async ({ page }) => {
  await page.goto("/content/article");
  // Other tests add untagged articles, so compare against whatever the unfiltered total is.
  const summary = page.getByText(/^Showing \d+–\d+ of \d+$/);
  const unfiltered = (await summary.textContent())!;

  const filter = page.getByPlaceholder("Filter by tag (matches any)");
  await filter.fill("sqlite");
  await filter.press("Enter");
  await expect(page.getByText("Showing 1–1 of 1")).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: "sqlite" })).toHaveCount(1);

  // Tags match any: adding a second tag widens the result.
  await filter.fill("workflow");
  await filter.press("Enter");
  await expect(page.getByText("Showing 1–2 of 2")).toBeVisible();

  await filter.fill("no-such-tag");
  await filter.press("Enter");
  await expect(page.getByText("Showing 1–2 of 2")).toBeVisible();

  await page.getByRole("button", { name: "Remove sqlite" }).click();
  await page.getByRole("button", { name: "Remove workflow" }).click();
  await page.getByRole("button", { name: "Remove no-such-tag" }).click();
  await expect(summary).toHaveText(unfiltered);
});
