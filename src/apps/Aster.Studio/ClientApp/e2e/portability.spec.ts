import { expect, test, type Locator, type Page } from "@playwright/test";
import { field, unique } from "./support";

// Smoke coverage for #112: snapshot export and import across tenants, the tenant switcher, and the query builder.
// The seed gives the Acme tenant its own page content type, so a page exported from the default tenant collides
// with it. Each test creates the resources it exports, imports or queries.

async function choose(page: Page, label: string, option: string, scope: Page | Locator = page) {
  await scope.getByRole("combobox", { name: label }).click();
  await page.getByRole("option", { name: option, exact: true }).click();
}

async function switchTenant(page: Page, name: "Default" | "Acme") {
  await choose(page, "Tenant", name);
  await expect(page.getByRole("combobox", { name: "Tenant" })).toHaveText(name);
}

const sidebarLink = (page: Page, name: string) => page.locator("aside").getByRole("link", { name: new RegExp(`^${name}`) });

const createPage = (page: Page, tenant: string, resourceId: string, title: string) =>
  page.request.post("/api/aster/resources", {
    headers: { "X-Aster-Tenant": tenant },
    data: { definitionId: "page", resourceId, aspects: { content: { title }, seo: { slug: resourceId } } },
  });

test("exports a page from the default tenant and imports it into Acme, remapping what collides", async ({ page }, testInfo) => {
  const resourceId = unique("shared");
  expect((await createPage(page, "default", resourceId, "Default copy")).ok()).toBe(true);
  expect((await createPage(page, "acme", resourceId, "Acme copy")).ok()).toBe(true);

  await page.goto("/portability");
  await choose(page, "Export scope", "Selected resources");
  await page.getByRole("textbox", { name: "Resources" }).fill(resourceId);
  await page.getByRole("textbox", { name: "Resources" }).press("Enter");
  await choose(page, "Versions", "Latest version only");
  await page.getByRole("button", { name: "Export", exact: true }).click();
  const exported = page.getByRole("region", { name: "Export result" });
  await expect(exported.getByText(/1 definition versions · 4 shared aspect versions · 1 resource versions/)).toBeVisible();
  const download = page.waitForEvent("download");
  await exported.getByRole("button", { name: "Download snapshot" }).click();
  const snapshot = testInfo.outputPath("snapshot.json");
  await (await download).saveAs(snapshot);

  await switchTenant(page, "Acme");
  await expect(page.getByRole("heading", { name: "Import into Acme" })).toBeVisible();
  await page.getByLabel("Snapshot file").setInputFiles(snapshot);

  // Strict mode refuses both divergent collisions, Acme's page type and its copy of the page, before writing.
  await page.getByRole("button", { name: "Preview import" }).click();
  const preview = page.getByRole("region", { name: "Import preview" });
  await expect(preview.getByRole("heading", { name: "Cannot import" })).toBeVisible();
  await expect(preview.getByRole("list", { name: "Diagnostics" }).getByText("definitions/page/1")).toBeVisible();
  await expect(preview.getByRole("list", { name: "Diagnostics" }).getByText(`resources/${resourceId}/1`)).toBeVisible();
  await expect(page.getByRole("button", { name: "Import into Acme" })).toBeDisabled();

  await choose(page, "Collisions", "Remap divergent content");
  await page.getByRole("button", { name: "Preview import" }).click();
  await expect(preview.getByRole("heading", { name: "Ready to import" })).toBeVisible();
  const mappings = preview.getByRole("table", { name: "Identity mappings" });
  await expect(mappings.getByRole("row").filter({ hasText: "page__imported v1" })).toContainText("remapped-divergent");
  await expect(mappings.getByRole("row").filter({ hasText: `${resourceId}__imported v1` })).toContainText("remapped-divergent");
  await expect(mappings.getByRole("row").filter({ hasText: "content v1" })).toContainText("reused-identical");
  await expect(preview.getByRole("list", { name: "Diagnostics" }).getByText("divergent-identity-collision").first()).toBeVisible();

  await page.getByRole("button", { name: "Import into Acme" }).click();
  const result = page.getByRole("region", { name: "Import result" });
  await expect(result.getByRole("heading", { name: "Imported" })).toBeVisible();

  // The page lands in Acme under the remapped IDs; Acme's own copy keeps its content.
  await sidebarLink(page, "Page__imported").click();
  await expect(page.getByRole("row").filter({ hasText: "Default copy" })).toContainText(`${resourceId}__imported`);
  await page.goto("/content/page");
  await expect(page.getByRole("row").filter({ hasText: "Acme copy" })).toContainText(resourceId);

  await switchTenant(page, "Default");
  await expect(sidebarLink(page, "Page__imported")).toHaveCount(0);
});

test("switching tenant scopes every screen and survives a reload", async ({ page }) => {
  const title = unique("Acme product");
  await page.goto("/");
  await expect(sidebarLink(page, "Article")).toBeVisible();
  await switchTenant(page, "Acme");
  await expect(sidebarLink(page, "Product")).toBeVisible();
  await expect(sidebarLink(page, "Article")).toHaveCount(0);

  // The editor writes into the selected tenant too.
  await page.goto("/content/product/new");
  await field(page, "content", "title").fill(title);
  await page.getByRole("button", { name: "Create resource" }).click();
  await expect(page).toHaveURL(/\/content\/product\/(?!new$)[^/]+$/);
  const resourceId = decodeURIComponent(new URL(page.url()).pathname.split("/").pop()!);

  await page.reload();
  await expect(page.getByRole("combobox", { name: "Tenant" })).toHaveText("Acme");
  await page.goto("/content/page");
  await expect(page.getByRole("row").filter({ hasText: "Acme Home" })).toBeVisible();

  await switchTenant(page, "Default");
  await page.goto("/content/page");
  await expect(page.getByRole("row").filter({ hasText: "page-home" })).not.toContainText("Acme Home");
  await expect(sidebarLink(page, "Product")).toHaveCount(0);
  expect((await page.request.get(`/api/aster/resources/${resourceId}`)).status()).toBe(404);
  expect((await page.request.get(`/api/aster/resources/${resourceId}`, { headers: { "X-Aster-Tenant": "acme" } })).ok()).toBe(true);
});

test("recovers from a stored tenant the host no longer seeds", async ({ page }) => {
  await page.goto("/");
  await page.evaluate(() => localStorage.setItem("aster-studio.tenant", "retired-tenant"));
  await page.reload();
  // The tenant list stays readable with the stale header, so the shell falls back to the default tenant.
  await expect(page.getByRole("combobox", { name: "Tenant" })).toHaveText("Default");
  await expect(sidebarLink(page, "Article")).toBeVisible();
});

test("runs a valid query and shows path-precise validation failures for an invalid one", async ({ page }) => {
  const tag = unique("query");
  const title = unique("Queried article");
  const created = await page.request.post("/api/aster/resources", {
    data: { definitionId: "article", aspects: { content: { title }, seo: { slug: tag }, taggable: { tags: [tag] } } },
  });
  expect(created.ok()).toBe(true);

  await page.goto("/query");
  await expect(page.getByLabel("Provider capabilities")).toContainText("Metadata range filters");
  await choose(page, "Content type", "Article");
  await page.getByRole("button", { name: "Add condition" }).click();
  const first = page.getByRole("group", { name: "Condition 1" });
  await choose(page, "Aspect", "taggable", first);
  await choose(page, "Facet", "tags · string[]", first);
  await choose(page, "Operator", "array-contains-any", first);
  await first.getByLabel("Values").fill(tag);
  const validation = page.getByRole("region", { name: "Validation" });
  await expect(validation.getByText("Valid for SQLite JSON")).toBeVisible();
  await page.getByRole("button", { name: "Run query" }).click();
  await expect(page.getByRole("table", { name: "Query results" }).getByRole("row").filter({ hasText: title })).toBeVisible();

  // Metadata range filters are a declared unsupported feature, so the validator fails the second condition's operator.
  await page.getByRole("button", { name: "Add condition" }).click();
  const second = page.getByRole("group", { name: "Condition 2" });
  await choose(page, "Kind", "Metadata field", second);
  await choose(page, "Field", "Version", second);
  await choose(page, "Operator", "range (unsupported)", second);
  await second.getByLabel("Minimum").fill("2");
  await expect(second.getByText("unsupported-comparison-operator")).toBeVisible();
  await expect(second.getByText("Filter.Operands[1].Operator")).toBeVisible();
  await expect(validation.getByText("1 validation failures")).toBeVisible();
  await expect(page.getByRole("button", { name: "Run query" })).toBeDisabled();
});
